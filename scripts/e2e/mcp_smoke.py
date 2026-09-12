#!/usr/bin/env python3
"""Staging MCP stdio smoke test: discovery, Northwind reads, one write, and cleanup.

Standard library only. Driven by scripts/e2e/test-mcp-smoke.sh; see that script for the
environment variables it accepts.
"""

import datetime
import json
import os
import pathlib
import subprocess
import sys
import threading
import uuid

PROTOCOL_VERSION = "2024-11-05"
READ_TIMEOUT_SECONDS = 90

CLIENT_ID = "NWIND"
PROJECT_ID = os.environ.get("TIMEPRO_MCP_SMOKE_PROJECT", "8W52M2")
TENANT = os.environ.get("TIMEPRO_MCP_SMOKE_TENANT", "ssw-staging")
CATEGORY_ID = os.environ.get("TIMEPRO_MCP_SMOKE_CATEGORY", "WEBDEV")

REPO_ROOT = pathlib.Path(__file__).resolve().parents[2]
DISCOVERY_GOLDEN = (
    REPO_ROOT
    / "tests"
    / "SSW.TimePro.Cli.Integration"
    / "Goldens"
    / "Mcp"
    / "Discovery"
    / "tools-list.default.json"
)


class SmokeFailure(Exception):
    pass


class McpProcess:
    """Newline-delimited JSON-RPC over the child's stdio, with stderr drained separately."""

    def __init__(self, command):
        self._process = subprocess.Popen(
            command,
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            bufsize=1,
        )
        self._next_id = 1
        self._stderr = []
        self._stderr_thread = threading.Thread(target=self._drain_stderr, daemon=True)
        self._stderr_thread.start()

    def _drain_stderr(self):
        for line in self._process.stderr:
            self._stderr.append(line.rstrip())

    @property
    def stderr(self):
        return "\n".join(self._stderr)

    def request(self, method, params=None):
        request_id = self._next_id
        self._next_id += 1
        frame = {"jsonrpc": "2.0", "id": request_id, "method": method}
        if params is not None:
            frame["params"] = params
        self._write(frame)
        return self._read_response(request_id)

    def notify(self, method, params=None):
        frame = {"jsonrpc": "2.0", "method": method}
        if params is not None:
            frame["params"] = params
        self._write(frame)

    def _write(self, frame):
        self._process.stdin.write(json.dumps(frame) + "\n")
        self._process.stdin.flush()

    def _read_response(self, request_id):
        deadline = threading.Event()
        timer = threading.Timer(READ_TIMEOUT_SECONDS, deadline.set)
        timer.start()
        try:
            while True:
                if deadline.is_set():
                    raise SmokeFailure(f"timed out waiting for response {request_id}")

                line = self._process.stdout.readline()
                if line == "":
                    raise SmokeFailure(
                        f"MCP host exited before answering {request_id}\nstderr:\n{self.stderr}"
                    )

                line = line.strip()
                if not line:
                    continue

                try:
                    frame = json.loads(line)
                except json.JSONDecodeError:
                    raise SmokeFailure(f"non-protocol output on stdout: {line}")

                if frame.get("id") != request_id:
                    continue  # notification or out-of-order response

                if "error" in frame:
                    raise SmokeFailure(f"{request_id} failed: {json.dumps(frame['error'])}")

                return frame["result"]
        finally:
            timer.cancel()

    def close(self):
        try:
            self._process.stdin.close()
            self._process.wait(timeout=10)
        except Exception:
            self._process.kill()


def initialize(process):
    result = process.request(
        "initialize",
        {
            "protocolVersion": PROTOCOL_VERSION,
            "capabilities": {},
            "clientInfo": {"name": "tp-mcp-smoke", "version": "1.0.0"},
        },
    )
    if "protocolVersion" not in result or "capabilities" not in result:
        raise SmokeFailure(f"initialize did not negotiate a session: {json.dumps(result)}")
    if "tools" not in result["capabilities"]:
        raise SmokeFailure("server did not advertise the tools capability")
    process.notify("notifications/initialized")
    return result


def list_tools(process):
    names = []
    cursor = None
    while True:
        params = {"cursor": cursor} if cursor else {}
        result = process.request("tools/list", params)
        names.extend(tool["name"] for tool in result["tools"])
        cursor = result.get("nextCursor")
        if not cursor:
            return names


def call_tool(process, name, arguments):
    result = process.request("tools/call", {"name": name, "arguments": arguments})

    if result.get("isError"):
        raise SmokeFailure(f"{name} returned isError: {json.dumps(result)}")

    text = next(
        (block["text"] for block in result.get("content", []) if block.get("type") == "text"),
        None,
    )
    if text is None:
        raise SmokeFailure(f"{name} returned no text content: {json.dumps(result)}")

    payload = json.loads(text)
    # Tools still answer some failures with a legacy embedded error payload.
    if isinstance(payload, dict) and "error" in payload:
        raise SmokeFailure(f"{name} returned an embedded error: {payload['error']}")

    return payload


def create_tool(process, name, arguments):
    try:
        return call_tool(process, name, arguments)
    except SmokeFailure as failure:
        raise SmokeFailure(
            f"{failure}\n"
            "    The MCP write path is not yet the CLI's: create_timesheet omits the sell price "
            "the CLI resolves from the client rate, so TimePro rejects it."
        ) from failure


def assert_non_production(tp_command):
    info = subprocess.run(
        tp_command + ["tenant", "info", "--tenant", TENANT, "--json"],
        capture_output=True,
        text=True,
        check=True,
    )
    tenant = json.loads(info.stdout)
    api_url = tenant.get("apiUrl", "")
    if tenant.get("isProduction", True):
        raise SmokeFailure(f"refusing to run: tenant {TENANT} resolves to production ({api_url})")
    print(f"  tenant {TENANT} resolves to a non-production host: {api_url}")
    return api_url


def check_discovery(names):
    if not DISCOVERY_GOLDEN.exists():
        raise SmokeFailure(f"discovery golden missing: {DISCOVERY_GOLDEN}")

    expected = {tool["name"] for tool in json.loads(DISCOVERY_GOLDEN.read_text())["tools"]}
    missing = sorted(expected - set(names))
    if missing:
        raise SmokeFailure(f"tools/list is missing golden tools: {missing}")

    print(f"  tools/list returned {len(names)} tools, all {len(expected)} default tools present")


def most_recent_weekday():
    day = datetime.date.today()
    while day.weekday() > 4:
        day -= datetime.timedelta(days=1)
    return day.isoformat()


def main():
    tp_command = json.loads(os.environ["TIMEPRO_MCP_SMOKE_TP"])
    api_url = assert_non_production(tp_command)

    token = uuid.uuid4().hex[:8]
    note = f"MCP smoke {token}, safe to delete"
    date = most_recent_weekday()
    created_id = None

    process = McpProcess(tp_command + ["mcp", "--tenant", TENANT])
    try:
        session = initialize(process)
        print(f"  initialize negotiated protocol {session['protocolVersion']}")

        check_discovery(list_tools(process))

        projects = call_tool(process, "get_projects_for_client", {"clientId": CLIENT_ID})
        project = next((p for p in projects if p.get("value") == PROJECT_ID), None)
        if project is None:
            raise SmokeFailure(f"project {PROJECT_ID} not found for client {CLIENT_ID}")
        print(f"  project {PROJECT_ID} found: {project.get('displayText')}")

        iterations = call_tool(process, "list_iterations", {"projectId": PROJECT_ID})
        if not iterations:
            raise SmokeFailure(f"project {PROJECT_ID} returned no iterations")
        iteration_id = iterations[0]["iterationId"]
        print(f"  using iteration {iteration_id} ({iterations[0].get('iterationName')})")

        rate = call_tool(process, "get_client_rate", {"clientId": CLIENT_ID, "date": date})
        if rate is None:
            raise SmokeFailure(f"no client rate for {CLIENT_ID} on {date}")
        print(f"  client rate present for {CLIENT_ID}")

        before = call_tool(process, "get_timesheets", {"date": date})
        print(f"  {len(before)} existing entries on {date}")

        # The write goes through MCP on purpose. create_timesheet does not send a sell price, so
        # a server that cannot derive one answers 400 and this gate stays red until the shared
        # create orchestration lands.
        create_tool(
            process,
            "create_timesheet",
            {
                "clientId": CLIENT_ID,
                "projectId": PROJECT_ID,
                "date": date,
                "startTime": "04:00",
                "endTime": "04:15",
                "description": note,
                "categoryId": CATEGORY_ID,
                "iterationId": iteration_id,
            },
        )

        after = call_tool(process, "get_timesheets", {"date": date})
        matches = [entry for entry in after if entry.get("notes") == note]
        if len(matches) != 1:
            raise SmokeFailure(
                f"expected exactly one entry noted '{note}' on {date}, found {len(matches)}"
            )

        created = matches[0]
        created_id = created["timeId"]
        for field, expected in (
            ("clientId", CLIENT_ID),
            ("projectId", PROJECT_ID),
            ("date", date),
        ):
            if created.get(field) != expected:
                raise SmokeFailure(
                    f"created entry {created_id} has {field}={created.get(field)!r}, expected {expected!r}"
                )
        print(f"  created and read back entry {created_id} on project {PROJECT_ID}")

        call_tool(process, "delete_timesheet", {"timesheetId": created_id, "date": date})

        remaining = call_tool(process, "get_timesheets", {"date": date})
        if any(entry.get("timeId") == created_id for entry in remaining):
            raise SmokeFailure(f"entry {created_id} still present after delete")
        created_id = None
        print("  deleted the smoke entry and verified its absence")

        print(f"MCP smoke passed against {api_url}")
        return 0
    except SmokeFailure as failure:
        print(f"MCP smoke FAILED: {failure}", file=sys.stderr)
        if created_id is not None:
            print(
                f"LEFTOVER TEST DATA: timesheet {created_id} on {date} "
                f"(note '{note}') was not deleted",
                file=sys.stderr,
            )
        if process.stderr:
            print(f"server stderr:\n{process.stderr}", file=sys.stderr)
        return 1
    finally:
        process.close()


if __name__ == "__main__":
    sys.exit(main())
