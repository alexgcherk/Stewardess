"""
Downloads the OpenAPI 3.0 spec from the running StewardessMCPService and
post-processes it for use as an Open WebUI OpenAPI Server tool.

The live service already exposes a valid OpenAPI 3.0 spec via Swashbuckle at
/swagger/v1/swagger.json — this script fetches it, filters out low-value
endpoints, and writes the result to a static file for easy sharing.

Usage:
    python generate_openapi3.py [base_url] [output_file]

Defaults:
    base_url    = http://localhost:55703
    output_file = stewardess_openapi3.json
"""

import json
import sys
import urllib.request

BASE_URL = sys.argv[1] if len(sys.argv) > 1 else "http://localhost:55703"
OUT_FILE = sys.argv[2] if len(sys.argv) > 2 else "stewardess_openapi3.json"

# ---------------------------------------------------------------------------
# Fetch OpenAPI 3.0 spec directly from the live service
# ---------------------------------------------------------------------------
url = f"{BASE_URL}/swagger/v1/swagger.json"
print(f"Fetching OpenAPI 3.0 spec from {url} ...")
with urllib.request.urlopen(url) as r:
    spec = json.loads(r.read().decode())

# ---------------------------------------------------------------------------
# Filter out low-value / noisy operations for LLM tool use
# ---------------------------------------------------------------------------
SKIP_OPS = {
    "Capabilities_GetTools",        # raw tool schema dump — not a callable tool
    "Health_GetVersion",
    "File_GetHash",
    "File_DetectEncoding",
    "File_DetectLineEnding",
    "Repository_FindConfigFiles",
    "Search_SearchByExtension",
    "Edit_PreviewChanges",
    "Edit_Rollback",
    "Edit_Patch",
    "Edit_ApplyDiff",
    "Edit_ApplyBatchEdits",
    "Edit_RenamePath",
    "Edit_MovePath",
    "Mcp_Dispatch",                 # JSON-RPC raw endpoint — not a REST tool
    "Mcp_ListTools",
    "Mcp_GetManifest",
    "Search_SearchTextGet",         # duplicate of POST variant; keep POST (fuller options)
}

filtered_paths = {}
for path, path_item in spec.get("paths", {}).items():
    new_path_item = {}
    for method, operation in path_item.items():
        if method.startswith("x-"):
            continue
        op_id = operation.get("operationId", "")
        if op_id in SKIP_OPS:
            continue
        new_path_item[method] = operation
    if new_path_item:
        filtered_paths[path] = new_path_item

spec["paths"] = filtered_paths

# Override server URL to match the provided base URL
spec["servers"] = [{"url": BASE_URL}]

# ---------------------------------------------------------------------------
# Write output
# ---------------------------------------------------------------------------
out = json.dumps(spec, indent=2, ensure_ascii=False)
with open(OUT_FILE, "w", encoding="utf-8") as f:
    f.write(out)

path_count   = len(spec["paths"])
op_count     = sum(len(pi) for pi in spec["paths"].values())
schema_count = len(spec.get("components", {}).get("schemas", {}))
print(f"Done. {op_count} operations across {path_count} paths, {schema_count} schemas.")
print(f"Output: {OUT_FILE}")
print(f"\nIn Open WebUI: Settings → Connections → Add OpenAPI Server")
print(f"  Title:  Stewardess MCP Service")
print(f"  URL:    {BASE_URL}")
print(f"  Path:   swagger/v1/swagger.json  (or upload the {OUT_FILE} file directly)")
