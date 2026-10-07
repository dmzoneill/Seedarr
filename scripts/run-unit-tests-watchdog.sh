#!/usr/bin/env bash
# Run unit tests with a hard wall-clock cap and 30s progress watchdog.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

UNIT_TEST="src/NzbDrone.Core.Test/Seedarr.Core.Test.csproj"
LOG="${UNIT_TEST_LOG:-/tmp/seedarr-unit-test.log}"
WALL_TIMEOUT_SEC="${UNIT_TEST_WALL_TIMEOUT_SEC:-600}"
STALL_SEC="${UNIT_TEST_STALL_SEC:-30}"

rm -f "$LOG"

echo "[watchdog] log=$LOG wall_timeout=${WALL_TIMEOUT_SEC}s stall=${STALL_SEC}s"

(
	timeout "$WALL_TIMEOUT_SEC" dotnet test "$UNIT_TEST" \
		--configuration Release \
		--no-build \
		--settings .runsettings \
		--blame-hang-timeout 90s \
		--logger "console;verbosity=minimal" \
		--logger "trx;LogFileName=test-results.trx" \
		--collect:"XPlat Code Coverage" \
		>"$LOG" 2>&1
	echo "EXIT:$?" >>"$LOG"
) &
TEST_PID=$!

last_lines=0
stall_count=0
while kill -0 "$TEST_PID" 2>/dev/null; do
	sleep "$STALL_SEC"
	lines=$(wc -l <"$LOG" 2>/dev/null || echo 0)
	if [[ "$lines" -le "$last_lines" ]]; then
		stall_count=$((stall_count + 1))
		echo "[watchdog] STALL #${stall_count} at $(date -u +%H:%M:%SZ) lines=$lines (no output for ${STALL_SEC}s)"
		pgrep -af 'testhost.dll|vstest.console' || true
		if [[ "$stall_count" -ge 3 ]]; then
			echo "[watchdog] killing hung test run (pid=$TEST_PID)"
			kill -TERM "$TEST_PID" 2>/dev/null || true
			pkill -f 'Seedarr.Core.Test.dll' 2>/dev/null || true
			pkill -f 'vstest.console' 2>/dev/null || true
			echo "HUNG" >>"$LOG"
			exit 124
		fi
	else
		stall_count=0
	fi
	last_lines=$lines
	tail -1 "$LOG" 2>/dev/null || true
done

wait "$TEST_PID"
exit_code=$?
tail -20 "$LOG" || true
exit "$exit_code"
