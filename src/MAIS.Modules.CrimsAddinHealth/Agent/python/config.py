# ---------------------------------------------------------------------------
# CyberArk — secret vault integration
# ---------------------------------------------------------------------------
CYBERARK_API_URL        = "https://cyberark.mfs.com/AIMWebService/api/accounts"
CYBERARK_APP_ID         = "Azure_OpenAI_API_Dev_APP"
CYBERARK_SAFE_NAME      = "Azure_OpenAI_API_Dev"
CYBERARK_MULE_ACCOUNT   = ""   # e.g. "STAGE_MULE_AOAI_API_CLIENT_ID_dc-..."
CYBERARK_AOAI_ACCOUNT   = ""   # e.g. "APIKEY_mfs-ai-svcs-api-stg"

# ---------------------------------------------------------------------------
# Mule API gateway
# ---------------------------------------------------------------------------
MULE_CLIENT_ID          = ""   # e.g. "dc-g7ap1ke6s9t60lpjtwlvppvbi"
MULE_GATEWAY_URL        = ""   # e.g. "https://apigateway-stage.mfs.com/ECP/git-aoai-sapi/v1/chat-completions"

# ---------------------------------------------------------------------------
# Azure OpenAI
# ---------------------------------------------------------------------------
AZURE_OPENAI_ENDPOINT   = ""
AZURE_OPENAI_DEPLOYMENT = ""   # e.g. "deploy-gpt-5-4"
AZURE_OPENAI_API_VERSION = ""  # e.g. "2024-12-01-preview"