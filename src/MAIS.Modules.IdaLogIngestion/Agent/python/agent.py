#!/usr/bin/env python3
"""
MAIS IdaLogIngestion AI Agent
Reviews novel log template patterns and produces structured enrichment for administrator review.
Called by C# via stdin/stdout JSON IPC. Reads one JSON line, writes one JSON line, exits.

AI backend: Azure OpenAI via Mule API gateway, secrets via CyberArk.
Falls back to heuristic analysis when config or secrets are unavailable.
"""

import sys
import json
import time
import logging
import requests

# All logging goes to stderr — stdout is reserved for JSON IPC.
logging.basicConfig(stream=sys.stderr, level=logging.INFO,
                    format="%(asctime)s [%(levelname)s] %(message)s")
log = logging.getLogger(__name__)

# ---------------------------------------------------------------------------
# Optional AI backend (requires config.py with CyberArk/Mule/AOAI constants)
# ---------------------------------------------------------------------------

try:
    from config import (
        CYBERARK_API_URL, CYBERARK_APP_ID, CYBERARK_SAFE_NAME,
        CYBERARK_MULE_ACCOUNT, CYBERARK_AOAI_ACCOUNT,
        MULE_CLIENT_ID, MULE_GATEWAY_URL,
        AZURE_OPENAI_DEPLOYMENT, AZURE_OPENAI_API_VERSION,
    )
    _CONFIG_AVAILABLE = True
except ImportError:
    _CONFIG_AVAILABLE = False
    log.warning("config.py not found; running in heuristic-only mode")


# ---------------------------------------------------------------------------
# CyberArk / Mule / Azure OpenAI helpers (mirrors CrimsAddinHealth agent)
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
        log.info("CyberArk: retrieved secret for '%s'", account_name)
        return res.json()["Content"]
    except Exception as exc:
        log.error("CyberArk fetch failed for '%s': %s", account_name, exc)
        return None


def _init_secrets():
    if not _CONFIG_AVAILABLE:
        return None, None
    return (
        _cyberark_fetch(CYBERARK_MULE_ACCOUNT, "MAIS IdaLogIngestion agent — Mule secret"),
        _cyberark_fetch(CYBERARK_AOAI_ACCOUNT, "MAIS IdaLogIngestion agent — AOAI key"),
    )


_mule_client_secret, _aoai_api_key = _init_secrets() if _CONFIG_AVAILABLE else (None, None)


def _call_aoai(system_message: str, user_prompt: str, retries: int = 2) -> str:
    if not _mule_client_secret or not _aoai_api_key:
        raise RuntimeError("CyberArk secrets unavailable — cannot call Azure OpenAI")

    url = (
        f"{MULE_GATEWAY_URL}"
        f"?deploymentId={AZURE_OPENAI_DEPLOYMENT}"
        f"&apiKey={_aoai_api_key}"
        f"&apiVersion={AZURE_OPENAI_API_VERSION}"
    )

    is_gpt5 = AZURE_OPENAI_DEPLOYMENT.startswith("deploy-gpt-5")
    payload = {
        "messages": [
            {"role": "system", "content": system_message},
            {"role": "user",   "content": user_prompt},
        ],
        **({"maxCompletionTokens": 1024, "temperature": 1}
           if is_gpt5 else
           {"maxTokens": 1024, "temperature": 0.2, "topP": 0.95}),
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
# Heuristic fallback (no AI required)
# ---------------------------------------------------------------------------

def _heuristic_review(token_pattern: list, sample_messages: list) -> dict:
    """Derives a best-effort enrichment from token structure when AI is unavailable."""
    readable = " ".join(("[VALUE]" if t == "*" else t) for t in token_pattern)
    description = f"Log entries matching the pattern: {readable}"
    if sample_messages:
        description += f". Example: \"{sample_messages[0][:200]}\""

    lower_tokens = " ".join(t for t in token_pattern if t != "*").lower()
    wildcard_ratio = (sum(1 for t in token_pattern if t == "*") / len(token_pattern)
                      if token_pattern else 0)

    if any(kw in lower_tokens for kw in ["error", "exception", "fail", "critical", "fatal"]):
        suggestion = "Ingest"
        rationale  = "Contains error/exception keywords — likely worth full retention."
    elif any(kw in lower_tokens for kw in ["warn", "warning"]):
        suggestion = "Ingest"
        rationale  = "Contains warning keywords — retaining for operational review."
    elif any(kw in lower_tokens for kw in ["debug", "trace", "verbose"]):
        suggestion = "Discard"
        rationale  = "Contains debug/trace keywords — typically low operational value."
    elif any(kw in lower_tokens for kw in ["heartbeat", "ping", "poll", "tick", "alive"]):
        suggestion = "StatsOnly"
        rationale  = "Appears to be a routine health/polling event — count only."
    elif wildcard_ratio > 0.6:
        suggestion = "StatsOnly"
        rationale  = "High wildcard ratio suggests a noisy variable-content event — count only."
    else:
        suggestion = "Ingest"
        rationale  = "Heuristic default — administrator review recommended."

    return {
        "humanReadableDescription":  description,
        "suggestedClassification":   suggestion,
        "rationale":                 f"Heuristic assessment (AI unavailable). {rationale}",
        "suggestedExtractionFields": [],
    }


# ---------------------------------------------------------------------------
# AI-powered review
# ---------------------------------------------------------------------------

def review_template(data: dict) -> dict:
    template_id     = data.get("templateId", "unknown")
    token_pattern   = data.get("tokenPattern", [])
    sample_messages = data.get("sampleMessages", [])

    if not _aoai_api_key or not _mule_client_secret:
        log.info("AI secrets unavailable — using heuristic review for %s", template_id)
        return _heuristic_review(token_pattern, sample_messages)

    readable  = " ".join(("[VALUE]" if t == "*" else t) for t in token_pattern)
    samples   = "\n".join(f"  - {m}" for m in sample_messages[:5])

    system_message = (
        "You are a log analysis expert for an investment management firm. "
        "You review novel log template patterns discovered in production log files "
        "and help administrators decide how to classify them."
    )

    user_prompt = f"""Review this novel log template pattern:

Template ID: {template_id}
Token pattern (literals + [VALUE] placeholders): {readable}
Sample log messages:
{samples if samples else "  (none provided)"}

Return ONLY valid JSON with these exact fields — no preamble, no markdown fences:
{{
  "humanReadableDescription": "1-2 sentence plain-English description of what this log event represents",
  "suggestedClassification": "Ingest | StatsOnly | Discard",
  "rationale": "One sentence explaining the classification recommendation",
  "suggestedExtractionFields": ["fieldName1", "fieldName2"]
}}

Classification guidelines:
- Ingest:    Business events, errors, warnings worth keeping full-text for search/alerting.
- StatsOnly: Repetitive operational noise (heartbeats, polling, routine audits) — count only.
- Discard:   Debug/trace output of no operational value in production."""

    raw = _call_aoai(system_message, user_prompt)

    try:
        return json.loads(raw)
    except json.JSONDecodeError:
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

        if action == "review_template":
            result = review_template(data)
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
