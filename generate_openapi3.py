"""
Converts the StewardessMCPService Swagger 2.0 spec to an OpenAPI 3.0 JSON
suitable for import into Open WebUI as an OpenAPI Server tool.

Usage:
    python generate_openapi3.py [base_url] [output_file]

Defaults:
    base_url    = http://localhost:55702
    output_file = stewardess_openapi3.json
"""

import json
import sys
import urllib.request
import copy
import re

BASE_URL = sys.argv[1] if len(sys.argv) > 1 else "http://localhost:55702"
OUT_FILE = sys.argv[2] if len(sys.argv) > 2 else "stewardess_openapi3.json"

# ---------------------------------------------------------------------------
# Fetch source spec
# ---------------------------------------------------------------------------
print(f"Fetching Swagger 2.0 spec from {BASE_URL}/swagger/docs/v1 ...")
with urllib.request.urlopen(f"{BASE_URL}/swagger/docs/v1") as r:
    v2 = json.loads(r.read().decode())

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------
def fix_refs(obj):
    """Recursively rewrite #/definitions/X → #/components/schemas/X"""
    if isinstance(obj, dict):
        return {
            k: fix_refs(v) if k != "$ref" else v.replace("#/definitions/", "#/components/schemas/")
            for k, v in obj.items()
        }
    if isinstance(obj, list):
        return [fix_refs(i) for i in obj]
    return obj

def convert_schema(schema):
    """Return a cleaned copy of a schema, fixing refs."""
    if schema is None:
        return {}
    s = copy.deepcopy(schema)
    s.pop("example", None)
    s.pop("x-nullable", None)
    return fix_refs(s)

def body_param(params):
    """Return the first body parameter, or None."""
    for p in (params or []):
        if p.get("in") == "body":
            return p
    return None

def non_body_params(params):
    return [p for p in (params or []) if p.get("in") != "body"]

# ---------------------------------------------------------------------------
# Build OpenAPI 3.0 skeleton
# ---------------------------------------------------------------------------
v3 = {
    "openapi": "3.0.3",
    "info": {
        "title": "Stewardess MCP Service",
        "description": (
            "Local source-code repository tools for AI agents. "
            "Provides file reading, search, editing, git, and command execution."
        ),
        "version": v2.get("info", {}).get("version", "2.0.0"),
    },
    "servers": [{"url": BASE_URL}],
    "paths": {},
    "components": {"schemas": {}},
}

# Convert definitions → components/schemas
for name, schema in v2.get("definitions", {}).items():
    v3["components"]["schemas"][name] = convert_schema(schema)

# ---------------------------------------------------------------------------
# Convert paths
# ---------------------------------------------------------------------------
SKIP_TAGS = set()          # skip no tags
SKIP_OPS  = {              # skip noisy / low-value ops for LLM use
    "Capabilities_GetTools",
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
    "Mcp_Dispatch",         # JSON-RPC raw endpoint – not useful as a REST tool
    "Mcp_ListTools",
    "Mcp_GetManifest",
    # Duplicate GET/POST variants - keep POST which has full options
    "Search_SearchTextGet",
}

for path, path_item in v2.get("paths", {}).items():
    new_path_item = {}

    for method, op in path_item.items():
        if method.startswith("x-"):
            continue
        op_id = op.get("operationId", "")
        if op_id in SKIP_OPS:
            continue

        params_v2   = op.get("parameters", [])
        body_p      = body_param(params_v2)
        query_params = non_body_params(params_v2)

        new_op = {
            "operationId": op_id,
            "summary":     op.get("summary", ""),
            "description": op.get("description", op.get("summary", "")),
            "tags":        op.get("tags", []),
        }

        # Query / path / header parameters
        if query_params:
            new_op["parameters"] = []
            for p in query_params:
                new_p = {
                    "name":        p["name"],
                    "in":          p["in"],
                    "required":    p.get("required", False),
                    "description": p.get("description", ""),
                    "schema":      convert_schema(p.get("schema") or {
                        "type": p.get("type", "string"),
                        "enum": p.get("enum"),
                    }),
                }
                # drop nulls
                if new_p["schema"].get("enum") is None:
                    new_p["schema"].pop("enum", None)
                new_op["parameters"].append(new_p)

        # Request body
        if body_p:
            body_schema = convert_schema(body_p.get("schema", {}))
            new_op["requestBody"] = {
                "required": body_p.get("required", True),
                "content": {
                    "application/json": {"schema": body_schema}
                },
            }

        # Responses
        new_op["responses"] = {}
        for code, resp in op.get("responses", {}).items():
            new_resp = {"description": resp.get("description", "")}
            schema = resp.get("schema")
            if schema:
                new_resp["content"] = {
                    "application/json": {"schema": convert_schema(schema)}
                }
            new_op["responses"][str(code)] = new_resp

        if not new_op["responses"]:
            new_op["responses"]["200"] = {"description": "Success"}

        new_path_item[method] = new_op

    if new_path_item:
        v3["paths"][path] = new_path_item

# ---------------------------------------------------------------------------
# Write output
# ---------------------------------------------------------------------------
out = json.dumps(v3, indent=2, ensure_ascii=False)
with open(OUT_FILE, "w", encoding="utf-8") as f:
    f.write(out)

path_count = len(v3["paths"])
op_count   = sum(len(pi) for pi in v3["paths"].values())
schema_count = len(v3["components"]["schemas"])
print(f"Done. {op_count} operations across {path_count} paths, {schema_count} schemas.")
print(f"Output: {OUT_FILE}")
print(f"\nIn Open WebUI: Settings → Connections → Add OpenAPI Server")
print(f"  Title: Stewardess MCP Service")
print(f"  URL:   {BASE_URL}/swagger/docs/v1  (or import the {OUT_FILE} file)")
