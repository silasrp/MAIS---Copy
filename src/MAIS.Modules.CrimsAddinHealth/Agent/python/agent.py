#!/usr/bin/env python3
"""
MAIS CrimsAddinHealth AI Agent
Analyses CRIMS addin scan results and produces structured update recommendations.
Called by C# via stdin/stdout JSON IPC. Reads one JSON line, writes one JSON line, exits.

AI backend: Azure OpenAI via Mule API gateway, secrets via CyberArk.
"""

import sys
import json
import time
import logging
import requests
from datetime import datetime, timezone
from config import (
    CYBERARK_API_URL, CYBERARK_APP_ID, CYBERARK_SAFE_NAME,
    CYBERARK_MULE_ACCOUNT, CYBERARK_AOAI_ACCOUNT,
    MULE_CLIENT_ID, MULE_GATEWAY_URL,
    AZURE_OPENAI_DEPLOYMENT, AZURE_OPENAI_API_VERSION,
)

# All logging goes to stderr — stdout is reserved for JSON IPC.
logging.basicConfig(stream=sys.stderr, level=logging.INFO,
                    format="%(asctime)s [%(levelname)s] %(message)s")
log = logging.getLogger(__name__)


# ---------------------------------------------------------------------------
# CyberArk helpers
# ---------------------------------------------------------------------------

def _cyberark_fetch(account_name: str, reason: str) -> str | None:
    try:
        res = requests.get(
            CYBERARK_API_URL,
            params={
                "AppID":  CYBERARK_APP_ID,
                "Reason": reason,
                "Query":  f"Safe={CYBERARK_SAFE_NAME};Object={account_name}",
            },
            timeout=10,
        )
        res.raise_for_status()
        log.info("CyberArk: retrieved secret for account '%s'", account_name)
        return res.json()["Content"]
    except Exception as exc:
        log.error("CyberArk fetch failed for '%s': %s", account_name, exc)
        return None


def get_mule_client_secret() -> str | None:
    return _cyberark_fetch(CYBERARK_MULE_ACCOUNT, "MAIS agent — retrieve Mule client secret")


def get_aoai_api_key() -> str | None:
    return _cyberark_fetch(CYBERARK_AOAI_ACCOUNT, "MAIS agent — retrieve AOAI API key")


# ---------------------------------------------------------------------------
# Secrets — fetched once at process startup
# ---------------------------------------------------------------------------

_mule_client_secret: str | None = get_mule_client_secret()
_aoai_api_key:       str | None = get_aoai_api_key()


# ---------------------------------------------------------------------------
# Gateway call
# ---------------------------------------------------------------------------

def _build_url() -> str:
    return (
        f"{MULE_GATEWAY_URL}"
        f"?deploymentId={AZURE_OPENAI_DEPLOYMENT}"
        f"&apiKey={_aoai_api_key}"
        f"&apiVersion={AZURE_OPENAI_API_VERSION}"
    )


def _call_aoai(system_message: str, user_prompt: str, retries: int = 2) -> str:
    """
    POST to Mule→Azure OpenAI and return the raw text content.
    Raises on unrecoverable failure.
    """
    if not _mule_client_secret or not _aoai_api_key:
        raise RuntimeError("CyberArk secrets unavailable — cannot call Azure OpenAI")

    url = _build_url()

    is_gpt5 = AZURE_OPENAI_DEPLOYMENT.startswith("deploy-gpt-5")

    if is_gpt5:
        payload = {
            "messages": [
                {"role": "system", "content": system_message},
                {"role": "user",   "content": user_prompt},
            ],
            "maxCompletionTokens": 1024,
            "temperature": 1,
        }
    else:
        payload = {
            "messages": [
                {"role": "system", "content": system_message},
                {"role": "user",   "content": user_prompt},
            ],
            "maxTokens": 1024,
            "temperature": 0.2,
            "topP": 0.95,
        }

    headers = {
        "client_id":     MULE_CLIENT_ID,
        "client_secret": _mule_client_secret,
        "Content-Type":  "application/json",
    }

    for attempt in range(retries + 1):
        try:
            log.info("Calling Azure OpenAI (attempt %d/%d)", attempt + 1, retries + 1)
            resp = requests.post(url, headers=headers, json=payload, timeout=30)
            resp.raise_for_status()
            return resp.json()["choices"][0]["message"]["content"]

        except requests.exceptions.Timeout:
            log.warning("Azure OpenAI timeout on attempt %d", attempt + 1)
            if attempt < retries:
                time.sleep(2)

        except Exception as exc:
            log.error("Azure OpenAI error: %s", exc)
            raise

    raise TimeoutError("Azure OpenAI did not respond after all retries")


# ---------------------------------------------------------------------------
# MAIS analysis logic
# ---------------------------------------------------------------------------

def analyze_scan(data: dict) -> dict:
    scan         = data["scanResult"]
    mismatches   = scan["mismatches"]
    machine_role = scan.get("machineRole", "Unknown")
    machine_name = scan.get("machineName", "Unknown")
    crims_user   = scan.get("crimsUserId", "Unknown")
    current_time = datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M UTC")

    dll_list = "\n".join(
        f"  - {m['fileName']}: installed {m.get('localVersion', m.get('installedVersion', '?'))} "
        f"→ expected {m.get('repositoryVersion', m.get('expectedVersion', '?'))}"
        + (" [MISSING]" if m.get("isMissing") else "")
        for m in mismatches
    )

    system_message = (
        "You are an enterprise IT support AI for an investment management firm. "
        "You analyse CRIMS (Charles River Investment Management System) addin DLL "
        "scan results and produce structured update recommendations."
    )

    user_prompt = f"""A scan has detected outdated CRIMS addin DLLs.

Machine: {machine_name}
CRIMS User: {crims_user}
Role: {machine_role}
Current time (UTC): {current_time}
DLLs needing update:
{dll_list}

Return ONLY valid JSON with these exact fields — no preamble, no markdown fences:
{{
  "approvalMessage": "2-3 sentence professional message for support staff. Include machine name, user, and DLL count. No markdown.",
  "riskLevel": "High | Normal | Low  (High if Trader role, Low if machine appears unused)",
  "riskRationale": "One sentence explaining the risk assessment.",
  "recommendedTiming": "Immediate | EndOfDay | NextBusinessDay",
  "timingRationale": "One sentence. For Trader roles avoid trading hours 09:30-16:00 ET weekdays.",
  "estimatedDurationSeconds": 60
}}"""

    raw = _call_aoai(system_message, user_prompt)

    try:
        return json.loads(raw)
    except json.JSONDecodeError:
        # Model returned extra text — strip markdown fences if present
        cleaned = raw.strip().removeprefix("```json").removeprefix("```").removesuffix("```").strip()
        return json.loads(cleaned)


# ---------------------------------------------------------------------------
# Entry point — stdin/stdout JSON IPC
# ---------------------------------------------------------------------------

def main():
    raw = sys.stdin.readline()
    if not raw.strip():
        sys.stderr.write("No input received\n")
        sys.exit(1)

    try:
        data   = json.loads(raw)
        action = data.get("action")

        if action == "analyze_scan":
            result = analyze_scan(data)
        else:
            sys.stderr.write(f"Unknown action: {action}\n")
            sys.exit(1)

        sys.stdout.write(json.dumps(result) + "\n")
        sys.stdout.flush()

    except Exception as exc:
        sys.stderr.write(f"Error processing request: {exc}\n")
        sys.exit(1)


if __name__ == "__main__":
    main()