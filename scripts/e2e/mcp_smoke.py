#!/usr/bin/env python3
"""Staging MCP stdio smoke test: discovery, Northwind reads, one write, and cleanup.

Standard library only. Driven by scripts/e2e/test-mcp-smoke.sh; see scripts/e2e/README.md for the
environment variables it requires.
"""

import datetime
import json
import os
import pathlib
import queue
import subprocess
import sys
import threading
import uuid

PROTOCOL_VERSION = "2024-11-05"
READ_TIMEOUT_SECONDS = 90

CLIENT_ID = "NWIND"
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


def required_env(name, purpose):
    value = os.environ.get(name, "").strip()
    if not value:
        raise SmokeFailure(
            f"{name} is not set. {purpose} "
            "It has no default on purpose: this is a public repository and the ids of real "
            "projects and iterations do not belong in it. See scripts/e2e/README.md."
        )
    return value


class McpProcess:
    """Newline-delimited JSON-RPC over the child's stdio.

    stdout is drained by its own thread into a queue so a wedged server times out instead of
    blocking the driver forever on a read that cannot be interrupted.
    """

    _EOF = object()

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
        self._stdout = queue.Queue()
        self._stderr = []
        threading.Thread(target=self._drain_stdout, daemon=True).start()
        threading.Thread(target=self._drain_stderr, daemon=True).start()

    def _drain_stdout(self):
        for line in self._process.stdout:
            self._stdout.put(line)
        self._stdout.put(self._EOF)

    def _drain_stderr(self):
        for line in self._process.stderr:
            self._stderr.append(line.rstrip())

    @property
    def stderr(self):
        return "\n".join(self._stderr)

    @property
    def alive(self):
        return self._process.poll() is None

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
        try:
            self._process.stdin.write(json.dumps(frame) + "\n")
            self._process.stdin.flush()
        except (BrokenPipeError, ValueError):
            raise SmokeFailure(
                f"MCP host is not accepting input (exit code {self._process.poll()})\n"
                f"stderr:\n{self.stderr}"
            )

    def _read_response(self, request_id):
        deadline = datetime.datetime.now() + datetime.timedelta(seconds=READ_TIMEOUT_SECONDS)

        while True:
            remaining = (deadline - datetime.datetime.now()).total_seconds()
            if remaining <= 0:
                self.kill()
                raise SmokeFailure(
                    f"timed out after {READ_TIMEOUT_SECONDS}s waiting for response {request_id}; "
                    f"terminated the MCP host\nstderr:\n{self.stderr}"
                )

            try:
                line = self._stdout.get(timeout=remaining)
            except queue.Empty:
                self.kill()
                raise SmokeFailure(
                    f"timed out after {READ_TIMEOUT_SECONDS}s waiting for response {request_id}; "
                    f"terminated the MCP host\nstderr:\n{self.stderr}"
                )

            if line is self._EOF:
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

    def kill(self):
        try:
            self._process.kill()
        except Exception:
            pass

    def close(self):
        try:
            self._process.stdin.close()
            self._process.wait(timeout=10)
        except Exception:
            self.kill()


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


def find_marked(process, date, note, project_id):
    """Rows on <date> carrying the run's unique marker. Never matches another run's data."""
    entries = call_tool(process, "get_timesheets", {"date": date})
    return [
        entry
        for entry in entries
        if entry.get("notes") == note and entry.get("projectId") == project_id
    ]


def run_smoke(process, state):
    date, note, project_id = state["date"], state["note"], state["projectId"]

    session = initialize(process)
    print(f"  initialize negotiated protocol {session['protocolVersion']}")

    check_discovery(list_tools(process))

    projects = call_tool(process, "get_projects_for_client", {"clientId": CLIENT_ID})
    if not any(p.get("value") == project_id for p in projects):
        raise SmokeFailure(
            f"TIMEPRO_MCP_SMOKE_PROJECT is not a project of client {CLIENT_ID} on {TENANT}"
        )
    print(f"  the configured {CLIENT_ID} project is present")

    iterations = call_tool(process, "list_iterations", {"projectId": project_id})
    if not iterations:
        raise SmokeFailure(
            "the configured project returned no iterations; the smoke needs one that uses them"
        )

    iteration_id = state["iterationId"]
    if iteration_id is None:
        iteration_id = iterations[0]["iterationId"]
    elif not any(str(i["iterationId"]) == str(iteration_id) for i in iterations):
        raise SmokeFailure(
            "TIMEPRO_MCP_SMOKE_ITERATION is not an iteration of the configured project"
        )
    print(f"  selected 1 of {len(iterations)} iterations on the project")

    rate = call_tool(process, "get_client_rate", {"clientId": CLIENT_ID, "date": date})
    if rate is None:
        raise SmokeFailure(f"no client rate for {CLIENT_ID} on {date}")
    print(f"  client rate present for {CLIENT_ID}")

    before = find_marked(process, date, note, project_id)
    if before:
        raise SmokeFailure(
            f"the marker note is already on {date}; refusing to write an ambiguous row"
        )
    print(f"  marker '{note}' is unused on {date}")

    # The write goes through MCP on purpose, so a create tool the server rejects keeps this gate
    # red instead of being routed around.
    state["createAttempted"] = True
    create_result = call_tool(
        process,
        "create_timesheet",
        {
            "clientId": CLIENT_ID,
            "projectId": project_id,
            "date": date,
            "startTime": "04:00",
            "endTime": "04:15",
            "description": note,
            "categoryId": CATEGORY_ID,
            "iterationId": iteration_id,
        },
    )

    matches = find_marked(process, date, note, project_id)
    if len(matches) != 1:
        raise SmokeFailure(
            f"expected exactly one entry noted '{note}' on {date}, found {len(matches)}"
        )

    created = matches[0]
    created_id = created["timeId"]
    reported_id = create_result.get("timesheetId") if isinstance(create_result, dict) else None
    if reported_id not in (None, created_id):
        raise SmokeFailure(
            f"create_timesheet reported id {reported_id} but the row read back is {created_id}"
        )
    for field, expected in (("clientId", CLIENT_ID), ("projectId", project_id), ("date", date)):
        if created.get(field) != expected:
            raise SmokeFailure(
                f"created entry {created_id} has {field}={created.get(field)!r}, "
                f"expected {expected!r}"
            )
    print(f"  created and read back entry {created_id}")


def cleanup(tp_command, process, state):
    """Delete the run's own row and prove it is gone. Returns True when nothing is left behind."""
    if not state["createAttempted"]:
        return True

    date, note, project_id = state["date"], state["note"], state["projectId"]

    # A wedged or crashed host still has to be cleaned up after, so recover over a fresh session.
    own_process = None
    if not process.alive:
        print("  MCP host is gone; starting a fresh session to clean up", file=sys.stderr)
        own_process = McpProcess(tp_command + ["mcp", "--tenant", TENANT])
        initialize(own_process)
        process = own_process

    try:
        matches = find_marked(process, date, note, project_id)
        if not matches:
            print("  nothing to clean up: the marker note is not on the day")
            return True

        if len(matches) > 1:
            ids = sorted(entry["timeId"] for entry in matches)
            raise SmokeFailure(
                f"{len(matches)} rows carry the marker note; refusing to guess which to delete: {ids}"
            )

        created_id = matches[0]["timeId"]
        call_tool(process, "delete_timesheet", {"timesheetId": created_id, "date": date})

        if find_marked(process, date, note, project_id):
            raise SmokeFailure(f"entry {created_id} is still present after delete")

        print(f"  cleaned up entry {created_id} and verified its absence")
        return True
    except SmokeFailure as failure:
        print(f"CLEANUP UNRESOLVED: {failure}", file=sys.stderr)
        print(
            f"LEFTOVER TEST DATA: look for timesheets on {date} noted '{note}' "
            f"and delete them with 'tp ts delete <id> --date {date} --tenant {TENANT}'",
            file=sys.stderr,
        )
        return False
    finally:
        if own_process is not None:
            own_process.close()


def main():
    try:
        tp_command = json.loads(os.environ["TIMEPRO_MCP_SMOKE_TP"])
        iteration = os.environ.get("TIMEPRO_MCP_SMOKE_ITERATION", "").strip()
        state = {
            "date": most_recent_weekday(),
            "note": f"MCP smoke {uuid.uuid4().hex[:8]}, safe to delete",
            "projectId": required_env(
                "TIMEPRO_MCP_SMOKE_PROJECT",
                f"Set it to a {CLIENT_ID} project on {TENANT} that uses iterations.",
            ),
            "iterationId": iteration or None,
            "createAttempted": False,
        }
        api_url = assert_non_production(tp_command)
    except SmokeFailure as failure:
        print(f"MCP smoke FAILED: {failure}", file=sys.stderr)
        return 1

    process = McpProcess(tp_command + ["mcp", "--tenant", TENANT])
    failed = False
    try:
        run_smoke(process, state)
    except SmokeFailure as failure:
        failed = True
        print(f"MCP smoke FAILED: {failure}", file=sys.stderr)
        if process.stderr:
            print(f"server stderr:\n{process.stderr}", file=sys.stderr)
    finally:
        resolved = cleanup(tp_command, process, state)
        process.close()

    if failed or not resolved:
        return 1

    print(f"MCP smoke passed against {api_url}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
