#!/bin/bash
# shellcheck disable=SC2016,SC2034
#
# Integration test for Seedarr webhook + indexer flow
# Requires: podman-compose stack running (all services healthy)
#
set -euo pipefail

SEEDARR_URL="http://localhost:9898"
SONARR_URL="http://localhost:8989"
RADARR_URL="http://localhost:7878"
LIDARR_URL="http://localhost:8686"
PROWLARR_URL="http://localhost:9696"

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

PASS=0
FAIL=0
TOTAL=0

assert() {
	local name="$1" condition="$2"
	TOTAL=$((TOTAL + 1))
	if eval "$condition"; then
		echo -e "  ${GREEN}PASS${NC}: $name"
		PASS=$((PASS + 1))
	else
		echo -e "  ${RED}FAIL${NC}: $name"
		FAIL=$((FAIL + 1))
	fi
}

TRANSMISSION_URL="http://localhost:9091"
TEST_TORRENT_HASH="e63e5567d9352b7b0d7d6d9271c0c5b2a303a059"

get_api_key() {
	curl -sf "http://localhost:$1/initialize.json" 2>/dev/null |
		python3 -c "import json,sys; print(json.load(sys.stdin).get('apiKey',''))" 2>/dev/null || echo ""
}

transmission_rpc() {
	local session_id
	session_id=$(curl -sf -D- "$TRANSMISSION_URL/transmission/rpc" 2>&1 |
		grep -i "x-transmission-session-id" | tr -d '\r' | awk '{print $2}')
	curl -sf "$TRANSMISSION_URL/transmission/rpc" \
		-H "X-Transmission-Session-Id: $session_id" \
		-H "Content-Type: application/json" \
		-d "$1"
}

cleanup_torrents() {
	local ids
	ids=$(curl -sf "$SEEDARR_URL/api/v1/torrent" | python3 -c "
import json,sys
for t in json.load(sys.stdin):
    name = t.get('name','')
    if 'Integration.Test' in name or 'VideoHive' in name or 'Matrix' in name:
        print(t['id'])
" 2>/dev/null || true)
	for id in $ids; do
		curl -sf -X DELETE "$SEEDARR_URL/api/v1/torrent/$id" >/dev/null 2>&1 || true
	done
}

cleanup_e2e() {
	cleanup_torrents
	transmission_rpc "{\"method\":\"torrent-remove\",\"arguments\":{\"ids\":[\"$TEST_TORRENT_HASH\"],\"delete-local-data\":true}}" >/dev/null 2>&1 || true
	local radarr_key
	radarr_key=$(get_api_key 7878)
	if [ -n "$radarr_key" ]; then
		# Remove from Radarr queue
		curl -sf "$RADARR_URL/api/v3/queue" -H "X-Api-Key: $radarr_key" | python3 -c "
import json,sys
for r in json.load(sys.stdin).get('records',[]):
    if 'E63E5567' in r.get('downloadId','').upper():
        print(r['id'])
" 2>/dev/null | while read -r qid; do
			curl -sf -X DELETE "$RADARR_URL/api/v3/queue/$qid?removeFromClient=true&blocklist=false" \
				-H "X-Api-Key: $radarr_key" >/dev/null 2>&1 || true
		done

		# Delete movie and re-add fresh to clear history/cutoff state
		local movie_id
		movie_id=$(curl -sf "$RADARR_URL/api/v3/movie" -H "X-Api-Key: $radarr_key" | python3 -c "
import json,sys
for m in json.load(sys.stdin):
    if m.get('tmdbId') == 603: print(m['id']); break
" 2>/dev/null || echo "")
		if [ -n "$movie_id" ]; then
			curl -sf -X DELETE "$RADARR_URL/api/v3/movie/$movie_id?deleteFiles=true" \
				-H "X-Api-Key: $radarr_key" >/dev/null 2>&1 || true
			sleep 1
			local movie_data
			movie_data=$(curl -sf "$RADARR_URL/api/v3/movie/lookup/tmdb?tmdbId=603" -H "X-Api-Key: $radarr_key" 2>/dev/null || echo "")
			if [ -n "$movie_data" ]; then
				echo "$movie_data" | python3 -c "
import json,sys
m = json.load(sys.stdin)
m['rootFolderPath'] = '/config/movies'
m['qualityProfileId'] = 1
m['monitored'] = True
m['addOptions'] = {'searchForMovie': False}
print(json.dumps(m))
" | curl -sf -X POST "$RADARR_URL/api/v3/movie" \
					-H "X-Api-Key: $radarr_key" \
					-H "Content-Type: application/json" \
					-d @- >/dev/null 2>&1 || true
			fi
		fi
	fi
}

# ─── Preflight ───────────────────────────────────────────
echo ""
echo "╔══════════════════════════════════════════╗"
echo "║   Seedarr Integration Test Suite         ║"
echo "╚══════════════════════════════════════════╝"
echo ""

echo "--- Preflight: service health ---"
assert "Seedarr healthy" \
	'curl -sf "$SEEDARR_URL/api/v1/system/status" > /dev/null'
assert "Sonarr healthy" \
	'curl -sf "$SONARR_URL/ping" > /dev/null'
assert "Radarr healthy" \
	'curl -sf "$RADARR_URL/ping" > /dev/null'
assert "Lidarr healthy" \
	'curl -sf "$LIDARR_URL/ping" > /dev/null'
assert "Prowlarr healthy" \
	'curl -sf "$PROWLARR_URL/ping" > /dev/null'

# ─── Test 1: API endpoints exist ─────────────────────────
echo ""
echo "--- Test 1: API endpoints ---"
assert "GET /arrconnections returns array" \
	'[ "$(curl -sf "$SEEDARR_URL/api/v1/arrconnections" | python3 -c "import json,sys; print(type(json.load(sys.stdin)).__name__)")" = "list" ]'
assert "GET /indexers returns array" \
	'[ "$(curl -sf "$SEEDARR_URL/api/v1/indexers" | python3 -c "import json,sys; print(type(json.load(sys.stdin)).__name__)")" = "list" ]'
assert "GET /torrent returns array" \
	'[ "$(curl -sf "$SEEDARR_URL/api/v1/torrent" | python3 -c "import json,sys; print(type(json.load(sys.stdin)).__name__)")" = "list" ]'

# ─── Test 2: Connections configured ──────────────────────
echo ""
echo "--- Test 2: Connections configured ---"
CONN_COUNT=$(curl -sf "$SEEDARR_URL/api/v1/arrconnections" | python3 -c "import json,sys; print(len(json.load(sys.stdin)))")
assert "At least 3 arr connections" '[ "$CONN_COUNT" -ge 3 ]'

IDX_COUNT=$(curl -sf "$SEEDARR_URL/api/v1/indexers" | python3 -c "import json,sys; print(len(json.load(sys.stdin)))")
assert "At least 1 indexer" '[ "$IDX_COUNT" -ge 1 ]'

# ─── Test 3: Indexer test ────────────────────────────────
echo ""
echo "--- Test 3: Indexer connectivity ---"
IDX_ID=$(curl -sf "$SEEDARR_URL/api/v1/indexers" | python3 -c "import json,sys; data=json.load(sys.stdin); print(data[0]['id'] if data else '')")
if [ -n "$IDX_ID" ]; then
	assert "Prowlarr indexer test passes" \
		'[ "$(curl -sf -X POST "$SEEDARR_URL/api/v1/indexers/$IDX_ID/test" | python3 -c "import json,sys; print(json.load(sys.stdin).get(\"success\",False))")" = "True" ]'
fi

# ─── Test 4: Arr connection tests ────────────────────────
echo ""
echo "--- Test 4: Arr connection tests ---"
for ARR_TYPE in Sonarr Radarr Lidarr; do
	CONN_ID=$(curl -sf "$SEEDARR_URL/api/v1/arrconnections" | python3 -c "
import json,sys
for c in json.load(sys.stdin):
    if c.get('arrType') == '$ARR_TYPE':
        print(c['id']); break
" 2>/dev/null || echo "")
	if [ -n "$CONN_ID" ]; then
		assert "$ARR_TYPE connection test passes" \
			'[ "$(curl -sf -X POST "$SEEDARR_URL/api/v1/arrconnections/$CONN_ID/test" | python3 -c "import json,sys; print(json.load(sys.stdin).get(\"success\",False))")" = "True" ]'
	fi
done

# ─── Test 5: Webhook — ignored event types ───────────────
echo ""
echo "--- Test 5: Webhook event filtering ---"
RESULT=$(curl -sf -X POST "$SEEDARR_URL/api/v1/webhook/arr" \
	-H "Content-Type: application/json" \
	-d '{
    "eventType": "Download",
    "instanceName": "Sonarr",
    "downloadId": "AAAAAAAABBBBBBBBCCCCCCCC11111111DEADBEEF"
  }')
assert "Non-Grab event ignored" \
	'[ "$(echo "$RESULT" | python3 -c "import json,sys; print(json.load(sys.stdin).get(\"message\",\"\"))")" = "Ignored event type: Download" ]'

RESULT=$(curl -sf -X POST "$SEEDARR_URL/api/v1/webhook/arr" \
	-H "Content-Type: application/json" \
	-d '{
    "eventType": "Rename",
    "instanceName": "Radarr",
    "downloadId": ""
  }')
assert "Rename event ignored" \
	'echo "$RESULT" | grep -q "Ignored event type"'

# ─── Test 6: Webhook — missing downloadId ────────────────
echo ""
echo "--- Test 6: Webhook validation ---"
RESULT=$(curl -sf -X POST "$SEEDARR_URL/api/v1/webhook/arr" \
	-H "Content-Type: application/json" \
	-d '{
    "eventType": "Grab",
    "instanceName": "Sonarr"
  }')
assert "Missing downloadId rejected" \
	'echo "$RESULT" | grep -q "No downloadId"'

# ─── Test 7: Webhook — Sonarr grab creates torrent ───────
echo ""
echo "--- Test 7: Sonarr webhook grab ---"
cleanup_torrents

SONARR_HASH="aabbccdd11223344556677889900aabb11223344"
RESULT=$(curl -sf -X POST "$SEEDARR_URL/api/v1/webhook/arr" \
	-H "Content-Type: application/json" \
	-d "{
    \"eventType\": \"Grab\",
    \"instanceName\": \"Sonarr\",
    \"applicationUrl\": \"http://sonarr.local:8989\",
    \"downloadClient\": \"Transmission\",
    \"downloadClientType\": \"Transmission\",
    \"downloadId\": \"$SONARR_HASH\",
    \"release\": {
      \"releaseTitle\": \"Integration.Test.Sonarr.S01E01.720p.WEB-DL\",
      \"indexer\": \"TestIndexer\",
      \"size\": 1073741824,
      \"quality\": \"HDTV-720p\",
      \"releaseGroup\": \"TestGroup\"
    }
  }")
assert "Sonarr grab accepted" \
	'echo "$RESULT" | python3 -c "import json,sys; print(json.load(sys.stdin).get(\"success\",False))" | grep -q "True"'
assert "Sonarr grab has correct hash" \
	'[ "$(echo "$RESULT" | python3 -c "import json,sys; print(json.load(sys.stdin).get(\"infoHash\",\"\"))")" = "$SONARR_HASH" ]'

# Verify torrent created
TORRENT=$(curl -sf "$SEEDARR_URL/api/v1/torrent" | python3 -c "
import json,sys
for t in json.load(sys.stdin):
    if '$SONARR_HASH' in t.get('infoHash',''):
        print(json.dumps(t)); break
" 2>/dev/null || echo "")
assert "Torrent created in Seedarr" '[ -n "$TORRENT" ]'
assert "Torrent name matches" \
	'echo "$TORRENT" | python3 -c "import json,sys; print(json.load(sys.stdin).get(\"name\",\"\"))" | grep -q "Integration.Test.Sonarr"'

# ─── Test 8: Webhook — dedup ────────────────────────────
echo ""
echo "--- Test 8: Deduplication ---"
RESULT=$(curl -sf -X POST "$SEEDARR_URL/api/v1/webhook/arr" \
	-H "Content-Type: application/json" \
	-d "{
    \"eventType\": \"Grab\",
    \"instanceName\": \"Sonarr\",
    \"applicationUrl\": \"http://sonarr.local:8989\",
    \"downloadId\": \"$SONARR_HASH\",
    \"release\": {
      \"releaseTitle\": \"Integration.Test.Sonarr.DUPE\",
      \"size\": 999
    }
  }")
assert "Duplicate hash detected" \
	'echo "$RESULT" | grep -q "already exists"'

# ─── Test 9: Webhook — Radarr grab ──────────────────────
echo ""
echo "--- Test 9: Radarr webhook grab ---"
RADARR_HASH="11223344556677889900aabbccddeeff00112233"
RESULT=$(curl -sf -X POST "$SEEDARR_URL/api/v1/webhook/arr" \
	-H "Content-Type: application/json" \
	-d "{
    \"eventType\": \"Grab\",
    \"instanceName\": \"Radarr\",
    \"applicationUrl\": \"http://radarr.local:7878\",
    \"downloadClient\": \"Transmission\",
    \"downloadId\": \"$RADARR_HASH\",
    \"release\": {
      \"releaseTitle\": \"Integration.Test.Radarr.2024.1080p.BluRay\",
      \"indexer\": \"TestIndexer\",
      \"size\": 5368709120,
      \"quality\": \"Bluray-1080p\"
    }
  }")
assert "Radarr grab accepted" \
	'echo "$RESULT" | python3 -c "import json,sys; print(json.load(sys.stdin).get(\"success\",False))" | grep -q "True"'

RADARR_TORRENT=$(curl -sf "$SEEDARR_URL/api/v1/torrent" | python3 -c "
import json,sys
for t in json.load(sys.stdin):
    if '$RADARR_HASH' in t.get('infoHash',''):
        print('found'); break
" 2>/dev/null || echo "")
assert "Radarr torrent created" '[ "$RADARR_TORRENT" = "found" ]'

# ─── Test 10: Webhook — Lidarr grab ─────────────────────
echo ""
echo "--- Test 10: Lidarr webhook grab ---"
LIDARR_HASH="ffeeddccbbaa99887766554433221100ffeeddcc"
RESULT=$(curl -sf -X POST "$SEEDARR_URL/api/v1/webhook/arr" \
	-H "Content-Type: application/json" \
	-d "{
    \"eventType\": \"Grab\",
    \"instanceName\": \"Lidarr\",
    \"applicationUrl\": \"http://lidarr.local:8686\",
    \"downloadClient\": \"Transmission\",
    \"downloadId\": \"$LIDARR_HASH\",
    \"release\": {
      \"releaseTitle\": \"Integration.Test.Lidarr.Artist.Album.FLAC\",
      \"indexer\": \"TestIndexer\",
      \"size\": 734003200,
      \"quality\": \"FLAC\"
    }
  }")
assert "Lidarr grab accepted" \
	'echo "$RESULT" | python3 -c "import json,sys; print(json.load(sys.stdin).get(\"success\",False))" | grep -q "True"'

LIDARR_TORRENT=$(curl -sf "$SEEDARR_URL/api/v1/torrent" | python3 -c "
import json,sys
for t in json.load(sys.stdin):
    if '$LIDARR_HASH' in t.get('infoHash',''):
        print('found'); break
" 2>/dev/null || echo "")
assert "Lidarr torrent created" '[ "$LIDARR_TORRENT" = "found" ]'

# ─── Test 11: Connection matching ────────────────────────
echo ""
echo "--- Test 11: Connection matching ---"
# Webhook with applicationUrl should match the right connection
RESULT=$(curl -sf -X POST "$SEEDARR_URL/api/v1/webhook/arr" \
	-H "Content-Type: application/json" \
	-d '{
    "eventType": "Grab",
    "instanceName": "Sonarr",
    "applicationUrl": "http://sonarr.local:8989",
    "downloadId": "0000000000000000000000000000000000000001",
    "release": {
      "releaseTitle": "Integration.Test.ConnMatch.S01E01",
      "size": 100
    }
  }')
assert "Connection matched by URL" \
	'echo "$RESULT" | grep -q "basic metadata"'

# ─── Test 12: Prowlarr connectivity from Seedarr ────────
echo ""
echo "--- Test 12: Prowlarr reachability ---"
# Auth is disabled on Prowlarr by the configure step — no API key needed
assert "Prowlarr API reachable from host" \
	'curl -sf "$PROWLARR_URL/api/v1/health" > /dev/null'

# Verify Prowlarr has the 3 apps configured (Sonarr, Radarr, Lidarr)
PROWLARR_APPS=$(curl -sf "$PROWLARR_URL/api/v1/applications" \
	| python3 -c "import json,sys; print(len(json.load(sys.stdin)))" 2>/dev/null || echo "0")
assert "Prowlarr has 3 apps configured" '[ "$PROWLARR_APPS" -ge 3 ]'

# ─── Test 13: Fixture serving ────────────────────────────
echo ""
echo "--- Test 13: Test torrent fixture ---"
assert "test.torrent served via HTTP" \
	'curl -sf -o /dev/null -w "%{http_code}" "$SEEDARR_URL/fixtures/test.torrent" | grep -q "200"'
assert "test.torrent correct size (25829 bytes)" \
	'[ "$(curl -sf -o /dev/null -w "%{size_download}" "$SEEDARR_URL/fixtures/test.torrent")" = "25829" ]'

# ─── Test 14: Transmission connectivity ──────────────────
echo ""
echo "--- Test 14: Transmission download client ---"
assert "Transmission web UI accessible" \
	'curl -sf -o /dev/null "http://localhost:9091/transmission/web/"'

# Verify Transmission configured in all *arr apps
for ARR in "Sonarr:8989:v3" "Radarr:7878:v3" "Lidarr:8686:v1"; do
	ARR_NAME=${ARR%%:*}
	ARR_REST=${ARR#*:}
	ARR_PORT=${ARR_REST%%:*}
	ARR_VER=${ARR_REST#*:}
	ARR_KEY=$(curl -sf "http://localhost:$ARR_PORT/initialize.json" 2>/dev/null | python3 -c "import json,sys; print(json.load(sys.stdin).get('apiKey',''))" 2>/dev/null || echo "")
	if [ -n "$ARR_KEY" ]; then
		DC_COUNT=$(curl -sf "http://localhost:$ARR_PORT/api/$ARR_VER/downloadclient" -H "X-Api-Key: $ARR_KEY" | python3 -c "import json,sys; print(len([c for c in json.load(sys.stdin) if 'Transmission' in c.get('name','')]))" 2>/dev/null || echo "0")
		assert "$ARR_NAME has Transmission configured" '[ "$DC_COUNT" -ge 1 ]'
	fi
done

# ─── Test 15: Upload test.torrent to Seedarr directly ───
echo ""
echo "--- Test 15: Direct torrent upload to Seedarr ---"
cleanup_torrents

UPLOAD_RESULT=$(curl -sf -X POST "$SEEDARR_URL/api/v1/torrent/upload" \
	-F "file=@tests/fixtures/test.torrent")
UPLOAD_HASH=$(echo "$UPLOAD_RESULT" | python3 -c "import json,sys; print(json.load(sys.stdin).get('infoHash',''))" 2>/dev/null || echo "")
assert "Torrent uploaded to Seedarr" '[ -n "$UPLOAD_HASH" ]'
assert "Torrent hash is correct" '[ "$UPLOAD_HASH" = "e63e5567d9352b7b0d7d6d9271c0c5b2a303a059" ]'

UPLOADED_NAME=$(echo "$UPLOAD_RESULT" | python3 -c "import json,sys; print(json.load(sys.stdin).get('name',''))" 2>/dev/null || echo "")
assert "Torrent name parsed correctly" 'echo "$UPLOADED_NAME" | grep -q "VideoHive"'

# Clean up the uploaded torrent
if [ -n "$UPLOAD_HASH" ]; then
	UPLOAD_ID=$(curl -sf "$SEEDARR_URL/api/v1/torrent" | python3 -c "
import json,sys
for t in json.load(sys.stdin):
    if t.get('infoHash') == '$UPLOAD_HASH':
        print(t['id']); break
" 2>/dev/null || echo "")
	if [ -n "$UPLOAD_ID" ]; then
		curl -sf -X DELETE "$SEEDARR_URL/api/v1/torrent/$UPLOAD_ID" >/dev/null 2>&1
	fi
fi

# ─── Test 16: 3-way cross-check (Sonarr webhook flow) ───
echo ""
echo "--- Test 16: 3-way cross-check ---"
cleanup_torrents
TEST_HASH="e63e5567d9352b7b0d7d6d9271c0c5b2a303a059"
TORRENT_URL="http://seedarr.local:9898/fixtures/test.torrent"

# Simulate what happens when Sonarr grabs: webhook fires with the torrent's info hash
RESULT=$(curl -sf -X POST "$SEEDARR_URL/api/v1/webhook/arr" \
	-H "Content-Type: application/json" \
	-d "{
    \"eventType\": \"Grab\",
    \"instanceName\": \"Sonarr\",
    \"applicationUrl\": \"http://sonarr.local:8989\",
    \"downloadClient\": \"Transmission\",
    \"downloadClientType\": \"Transmission\",
    \"downloadId\": \"E63E5567D9352B7B0D7D6D9271C0C5B2A303A059\",
    \"release\": {
      \"releaseTitle\": \"VideoHive - Documentary Style Pack - 58710217\",
      \"indexer\": \"NoNaMe Club\",
      \"size\": 158649340,
      \"quality\": \"Unknown\"
    }
  }")
assert "Webhook grab with real hash accepted" \
	'echo "$RESULT" | python3 -c "import json,sys; print(json.load(sys.stdin).get(\"success\",False))" | grep -q "True"'

# Check 1: Torrent exists in Seedarr
SEEDARR_CHECK=$(curl -sf "$SEEDARR_URL/api/v1/torrent" | python3 -c "
import json,sys
for t in json.load(sys.stdin):
    if '$TEST_HASH' in t.get('infoHash',''):
        print(json.dumps({'name': t['name'], 'hash': t['infoHash'], 'size': t['totalSize']}))
        break
" 2>/dev/null || echo "")
assert "Torrent in Seedarr" '[ -n "$SEEDARR_CHECK" ]'
assert "Seedarr has correct name" 'echo "$SEEDARR_CHECK" | grep -q "VideoHive"'
assert "Seedarr has correct size" 'echo "$SEEDARR_CHECK" | grep -q "158649340"'

# Check 2: Verify Seedarr matched the Sonarr connection
assert "Webhook result has info hash" \
	'[ "$(echo "$RESULT" | python3 -c "import json,sys; print(json.load(sys.stdin).get(\"infoHash\",\"\"))")" = "$TEST_HASH" ]'

# Check 3: Verify dedup works with same real hash
DEDUP_RESULT=$(curl -sf -X POST "$SEEDARR_URL/api/v1/webhook/arr" \
	-H "Content-Type: application/json" \
	-d "{
    \"eventType\": \"Grab\",
    \"instanceName\": \"Radarr\",
    \"applicationUrl\": \"http://radarr.local:7878\",
    \"downloadId\": \"E63E5567D9352B7B0D7D6D9271C0C5B2A303A059\",
    \"release\": {
      \"releaseTitle\": \"VideoHive.DUPE\",
      \"size\": 1
    }
  }")
assert "Real hash dedup works" 'echo "$DEDUP_RESULT" | grep -q "already exists"'

# ─── Test 17: Real E2E — Radarr release/push → Transmission → Seedarr ───
echo ""
echo "--- Test 17: Real E2E flow (Radarr → Transmission → Seedarr) ---"
cleanup_e2e

RADARR_KEY=$(get_api_key 7878)
E2E_SKIP=false

if [ -z "$RADARR_KEY" ]; then
	echo -e "  ${YELLOW}SKIP${NC}: Cannot get Radarr API key"
	E2E_SKIP=true
fi

if [ "$E2E_SKIP" = "false" ]; then
	# cleanup_e2e already deleted/re-added The Matrix fresh — verify it exists
	sleep 2
	MOVIE_EXISTS=$(curl -sf "$RADARR_URL/api/v3/movie" -H "X-Api-Key: $RADARR_KEY" | python3 -c "
import json,sys
for m in json.load(sys.stdin):
    if m.get('tmdbId') == 603: print('yes'); break
" 2>/dev/null || echo "no")
	if [ "$MOVIE_EXISTS" != "yes" ]; then
		echo -e "  ${YELLOW}SKIP${NC}: The Matrix not in Radarr library"
		E2E_SKIP=true
	fi
fi

if [ "$E2E_SKIP" = "false" ]; then
	# Push release to Radarr — triggers: grab → Transmission → webhook → Seedarr
	TORRENT_URL="http://seedarr.local:9898/fixtures/test.torrent"
	PUSH_RESULT=$(curl -sf -X POST "$RADARR_URL/api/v3/release/push" \
		-H "X-Api-Key: $RADARR_KEY" \
		-H "Content-Type: application/json" \
		-d "{
      \"title\": \"The.Matrix.1999.1080p.BluRay.x264-TestGroup\",
      \"downloadUrl\": \"$TORRENT_URL\",
      \"protocol\": \"torrent\",
      \"publishDate\": \"2024-01-01T00:00:00Z\",
      \"size\": 158649340,
      \"indexer\": \"TestFixture\"
    }" 2>/dev/null || echo "")

	PUSH_APPROVED=$(echo "$PUSH_RESULT" | python3 -c "
import json,sys
data = json.load(sys.stdin)
if isinstance(data, list):
    print(data[0].get('approved', False) if data else False)
elif isinstance(data, dict):
    print(data.get('approved', False))
else:
    print(False)
" 2>/dev/null || echo "False")

	assert "Radarr release/push accepted" '[ "$PUSH_APPROVED" = "True" ]'

	if [ "$PUSH_APPROVED" = "True" ]; then
		# Wait for full chain: Radarr grab → Transmission add → webhook → Seedarr enrichment
		echo -e "  ${YELLOW}Waiting 25s for async enrichment chain...${NC}"
		sleep 25

		# Check 1: Radarr queue has the download
		RADARR_QUEUE=$(curl -sf "$RADARR_URL/api/v3/queue" -H "X-Api-Key: $RADARR_KEY" | python3 -c "
import json,sys
for r in json.load(sys.stdin).get('records',[]):
    did = r.get('downloadId','')
    if 'E63E5567' in did.upper():
        print(json.dumps({'downloadId': did, 'title': r.get('title',''), 'status': r.get('status','')}))
        break
" 2>/dev/null || echo "")
		assert "Torrent in Radarr queue" '[ -n "$RADARR_QUEUE" ]'

		# Check 2: Transmission has the torrent
		TRANS_CHECK=$(transmission_rpc "{\"method\":\"torrent-get\",\"arguments\":{\"ids\":[\"$TEST_TORRENT_HASH\"],\"fields\":[\"hashString\",\"name\",\"totalSize\"]}}" 2>/dev/null || echo "")
		TRANS_HASH=$(echo "$TRANS_CHECK" | python3 -c "
import json,sys
data = json.load(sys.stdin)
torrents = data.get('arguments',{}).get('torrents',[])
if torrents:
    print(torrents[0].get('hashString',''))
" 2>/dev/null || echo "")
		assert "Torrent in Transmission" '[ "$TRANS_HASH" = "$TEST_TORRENT_HASH" ]'

		TRANS_SIZE=$(echo "$TRANS_CHECK" | python3 -c "
import json,sys
data = json.load(sys.stdin)
torrents = data.get('arguments',{}).get('torrents',[])
if torrents:
    print(torrents[0].get('totalSize',0))
" 2>/dev/null || echo "0")
		assert "Transmission size matches" '[ "$TRANS_SIZE" = "158649340" ]'

		# Check 3: Seedarr has torrent with FULL enriched metadata
		SEEDARR_E2E=$(curl -sf "$SEEDARR_URL/api/v1/torrent" | python3 -c "
import json,sys
for t in json.load(sys.stdin):
    if '$TEST_TORRENT_HASH' in t.get('infoHash',''):
        print(json.dumps(t)); break
" 2>/dev/null || echo "")
		assert "Torrent in Seedarr" '[ -n "$SEEDARR_E2E" ]'

		if [ -n "$SEEDARR_E2E" ]; then
			E2E_NAME=$(echo "$SEEDARR_E2E" | python3 -c "import json,sys; print(json.load(sys.stdin).get('name',''))" 2>/dev/null || echo "")
			E2E_PIECE_COUNT=$(echo "$SEEDARR_E2E" | python3 -c "import json,sys; print(json.load(sys.stdin).get('pieceCount',0))" 2>/dev/null || echo "0")
			E2E_PIECE_LENGTH=$(echo "$SEEDARR_E2E" | python3 -c "import json,sys; print(json.load(sys.stdin).get('pieceLength',0))" 2>/dev/null || echo "0")

			assert "Seedarr name enriched from .torrent" 'echo "$E2E_NAME" | grep -q "VideoHive"'
			assert "Seedarr pieceCount from .torrent (1211)" '[ "$E2E_PIECE_COUNT" = "1211" ]'
			assert "Seedarr pieceLength from .torrent (131072)" '[ "$E2E_PIECE_LENGTH" = "131072" ]'
		fi

		echo ""
		echo "  3-way cross-check summary:"
		echo "    Radarr queue: $(echo "$RADARR_QUEUE" | python3 -c "import json,sys; print(json.load(sys.stdin).get('title','?'))" 2>/dev/null || echo "?")"
		echo "    Transmission: hash=$TRANS_HASH size=$TRANS_SIZE"
		echo "    Seedarr:      name=$E2E_NAME pieces=$E2E_PIECE_COUNT"
	else
		echo -e "  ${YELLOW}SKIP${NC}: Release not approved — skipping E2E cross-check"
	fi
fi

# ─── Test 18: Config API — CRUD for all settings sections ───
echo ""
echo "--- Test 18: Config API — CRUD for all settings sections ---"

config_sections=(
	"general"
	"seeding"
	"network"
	"bittorrent"
	"peerprotocol"
	"protocols"
	"simulation"
	"trackerserver"
	"scheduler"
	"advanced"
)

for section in "${config_sections[@]}"; do
	RESP=$(curl -sf "$SEEDARR_URL/api/v1/config/$section" 2>/dev/null || echo "")
	assert "GET config/$section returns JSON with id=1" \
		'echo "$RESP" | python3 -c "import json,sys; d=json.load(sys.stdin); assert d.get(\"id\")==1, f\"id={d.get(\"id\")}\"" 2>/dev/null'
done

# GET by id (singleton)
RESP=$(curl -sf "$SEEDARR_URL/api/v1/config/advanced/1" 2>/dev/null || echo "")
assert "GET config/advanced/1 returns singleton" \
	'echo "$RESP" | python3 -c "import json,sys; d=json.load(sys.stdin); assert d.get(\"id\")==1" 2>/dev/null'

# PUT round-trip: change uiRefreshRateSec, verify it persists
ORIG_RATE=$(curl -sf "$SEEDARR_URL/api/v1/config/advanced" | python3 -c "import json,sys; print(json.load(sys.stdin).get('uiRefreshRateSec',9))" 2>/dev/null)
NEW_RATE=$((ORIG_RATE + 1))
PUT_STATUS=$(curl -sf -o /dev/null -w "%{http_code}" -X PUT "$SEEDARR_URL/api/v1/config/advanced/1" \
	-H "Content-Type: application/json" \
	-d "{\"id\":1,\"logToFile\":true,\"fileLogLevel\":\"Info\",\"debugMode\":false,\"uiRefreshRateSec\":$NEW_RATE}" 2>/dev/null)
assert "PUT config/advanced/1 returns 202 Accepted" \
	'[ "$PUT_STATUS" = "202" ]'

UPDATED_RATE=$(curl -sf "$SEEDARR_URL/api/v1/config/advanced" | python3 -c "import json,sys; print(json.load(sys.stdin).get('uiRefreshRateSec',0))" 2>/dev/null)
assert "Config round-trip: uiRefreshRateSec updated to $NEW_RATE" \
	'[ "$UPDATED_RATE" = "$NEW_RATE" ]'

# Restore original value
curl -sf -X PUT "$SEEDARR_URL/api/v1/config/advanced/1" \
	-H "Content-Type: application/json" \
	-d "{\"id\":1,\"logToFile\":true,\"fileLogLevel\":\"Info\",\"debugMode\":false,\"uiRefreshRateSec\":$ORIG_RATE}" >/dev/null 2>&1

# PUT round-trip: seeding config
ORIG_SPEED=$(curl -sf "$SEEDARR_URL/api/v1/config/seeding" | python3 -c "import json,sys; print(json.load(sys.stdin).get('maxUploadSpeedKbps',0))" 2>/dev/null)
SEEDING_BODY=$(curl -sf "$SEEDARR_URL/api/v1/config/seeding" | python3 -c "
import json,sys
d=json.load(sys.stdin)
d['maxUploadSpeedKbps']=12345
print(json.dumps(d))
" 2>/dev/null)
PUT_STATUS=$(curl -sf -o /dev/null -w "%{http_code}" -X PUT "$SEEDARR_URL/api/v1/config/seeding/1" \
	-H "Content-Type: application/json" \
	-d "$SEEDING_BODY" 2>/dev/null)
assert "PUT config/seeding/1 returns 202" \
	'[ "$PUT_STATUS" = "202" ]'

UPDATED_SPEED=$(curl -sf "$SEEDARR_URL/api/v1/config/seeding" | python3 -c "import json,sys; print(json.load(sys.stdin).get('maxUploadSpeedKbps',0))" 2>/dev/null)
assert "Config round-trip: maxUploadSpeedKbps updated to 12345" \
	'[ "$UPDATED_SPEED" = "12345" ]'

# Restore original
SEEDING_BODY=$(curl -sf "$SEEDARR_URL/api/v1/config/seeding" | python3 -c "
import json,sys
d=json.load(sys.stdin)
d['maxUploadSpeedKbps']=$ORIG_SPEED
print(json.dumps(d))
" 2>/dev/null)
curl -sf -X PUT "$SEEDARR_URL/api/v1/config/seeding/1" \
	-H "Content-Type: application/json" \
	-d "$SEEDING_BODY" >/dev/null 2>&1

# PUT validation: negative port should fail
VAL_STATUS=$(curl -s -o /dev/null -w "%{http_code}" -X PUT "$SEEDARR_URL/api/v1/config/network/1" \
	-H "Content-Type: application/json" \
	-d '{"id":1,"listeningPort":0,"upnpEnabled":true,"maxGlobalConnections":200,"maxPerTorrentConnections":50,"maxUploadSlots":4,"proxyType":"none","proxyHost":"","proxyPort":8080,"proxyAuthEnabled":false,"proxyUsername":"","proxyPassword":""}' 2>/dev/null)
assert "PUT config/network with invalid port returns 400" \
	'[ "$VAL_STATUS" = "400" ]'

# ─── Cleanup ─────────────────────────────────────────────
echo ""
echo "--- Cleanup ---"
cleanup_e2e
echo -e "  ${GREEN}Cleaned up test torrents and E2E state${NC}"

# ─── Results ─────────────────────────────────────────────
echo ""
echo "══════════════════════════════════════════"
if [ "$FAIL" -eq 0 ]; then
	echo -e "  ${GREEN}ALL $TOTAL TESTS PASSED${NC}"
else
	echo -e "  ${RED}$FAIL/$TOTAL TESTS FAILED${NC} ($PASS passed)"
fi
echo "══════════════════════════════════════════"
echo ""

exit "$FAIL"
