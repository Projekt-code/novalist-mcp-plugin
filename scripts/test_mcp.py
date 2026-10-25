#!/usr/bin/env python3
"""Test the local MCP endpoint. No external libraries or project writes."""
import argparse
import json
import sys
import urllib.error
import urllib.request
from pathlib import Path

URL = "http://127.0.0.1:8765/mcp/"


def call(token: str, method: str, params: dict | None = None, request_id: int = 1):
    packet = {"jsonrpc": "2.0", "id": request_id, "method": method}
    if params is not None:
        packet["params"] = params
    req = urllib.request.Request(
        URL, data=json.dumps(packet).encode(),
        headers={"Authorization": "Bearer " + token,
                 "Content-Type": "application/json",
                 "Accept": "application/json, text/event-stream"},
        method="POST")
    with urllib.request.urlopen(req, timeout=45) as result:
        response = json.load(result)
    if "error" in response:
        raise RuntimeError(response["error"])
    return response.get("result", {})


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--token-file", type=Path, help="File in Novalist's extension settings folder")
    parser.add_argument("--chapter", type=int, default=8)
    parser.add_argument("--scene", type=int, default=1)
    parser.add_argument("--diagnose", action="store_true", help="Only show SDK read API signatures; no manuscript text")
    args = parser.parse_args()
    if not args.token_file:
        parser.error("Specify --token-file /path/to/bridge-token.txt. Find it under ~/.config/Novalist.")
    token = args.token_file.read_text(encoding="utf-8-sig").strip()
    try:
        hello = call(token, "initialize", {"protocolVersion": "2025-06-18", "capabilities": {},
                                           "clientInfo": {"name": "novalist-bridge-test", "version": "0.3"}})
        print("Handshake:", json.dumps(hello.get("serverInfo")))
        tools = call(token, "tools/list")["tools"]
        print("Tools:", ", ".join(t["name"] for t in tools))
        if args.diagnose:
            diagnostic = call(token, "tools/call", {"name": "sdk_diagnostics", "arguments": {}})
            info = json.loads(diagnostic["content"][0]["text"])
            if diagnostic.get("isError"):
                raise RuntimeError(info)
            print(json.dumps(info, indent=2))
            print("PASS: SDK metadata collected. No manuscript text read.")
            return 0
        structure = call(token, "tools/call", {"name": "list_chapters", "arguments": {}})
        parsed_structure = json.loads(structure["content"][0]["text"])
        if structure.get("isError"):
            raise RuntimeError(parsed_structure)
        print("Chapters:", len(parsed_structure["chapters"]))
        scene = call(token, "tools/call", {"name": "get_scene", "arguments":
                                          {"chapter": args.chapter, "scene": args.scene}})
        data = json.loads(scene["content"][0]["text"])
        if scene.get("isError"):
            raise RuntimeError(data)
        print(f"Chapter {data['chapter']}, Scene {data['scene']}: {data['sceneTitle']}")
        print("Characters:", data["totalCharacters"])
        print("Preview:", data["text"][:450].replace("\n", " "))
    except (urllib.error.URLError, RuntimeError, KeyError) as ex:
        print("FAIL:", ex, file=sys.stderr)
        return 1
    print("PASS: MCP handshake and scene retrieval completed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
