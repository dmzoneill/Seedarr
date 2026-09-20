#!/usr/bin/env bash
set -euo pipefail

RUN_ID="${1}"
echo "Monitoring workflow run ${RUN_ID} at job level..."

while true; do
  JOBS_JSON=$(gh api "repos/dmzoneill/Seedarr/actions/runs/${RUN_ID}/jobs" 2>/dev/null || echo "")
  if [ -z "${JOBS_JSON}" ]; then
    sleep 10
    continue
  fi

  # Parse jobs
  FAILED_JOBS=$(echo "${JOBS_JSON}" | jq -r '.jobs[] | select(.conclusion == "failure") | "\(.name) (id: \(.id))"')
  IN_PROGRESS_JOBS=$(echo "${JOBS_JSON}" | jq -r '.jobs[] | select(.status == "in_progress") | .name')
  QUEUED_JOBS=$(echo "${JOBS_JSON}" | jq -r '.jobs[] | select(.status == "queued") | .name')
  
  if [ -n "${FAILED_JOBS}" ]; then
    echo "=========================================="
    echo "ALERT: One or more jobs failed in run ${RUN_ID}:"
    echo "${FAILED_JOBS}"
    echo "=========================================="
    
    # Download and print failure logs
    for JOB_ID in $(echo "${JOBS_JSON}" | jq -r '.jobs[] | select(.conclusion == "failure") | .id'); do
      JOB_NAME=$(echo "${JOBS_JSON}" | jq -r ".jobs[] | select(.id == ${JOB_ID}) | .name")
      echo "--- Failure logs for: ${JOB_NAME} (${JOB_ID}) ---"
      gh api --allow-escape-sequences "repos/dmzoneill/Seedarr/actions/jobs/${JOB_ID}/logs" > "/tmp/job_${JOB_ID}.log" 2>/dev/null || true
      grep -E "Failed |Error |Exception |Build FAILED" "/tmp/job_${JOB_ID}.log" | tail -n 30 || true
    done
    exit 1
  fi

  # Check overall run status
  RUN_STATUS=$(gh api "repos/dmzoneill/Seedarr/actions/runs/${RUN_ID}" --jq '.status' 2>/dev/null || echo "in_progress")
  RUN_CONCLUSION=$(gh api "repos/dmzoneill/Seedarr/actions/runs/${RUN_ID}" --jq '.conclusion' 2>/dev/null || echo "")

  if [ "${RUN_STATUS}" = "completed" ]; then
    if [ "${RUN_CONCLUSION}" = "success" ]; then
      echo "SUCCESS: Workflow run ${RUN_ID} completed successfully!"
      exit 0
    else
      echo "Workflow run ${RUN_ID} completed with conclusion: ${RUN_CONCLUSION}"
      exit 1
    fi
  fi

  TIMESTAMP=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
  echo "[${TIMESTAMP}] Active: [${IN_PROGRESS_JOBS}] Queued: [${QUEUED_JOBS}]"
  sleep 20
done
