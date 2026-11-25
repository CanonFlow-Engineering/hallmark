#!/usr/bin/env bash
set -euo pipefail
source fixture/common.sh

kafka=$(jq -er '.kafka.container' target.json)
search=$(jq -er '.search.container' target.json)
topic=portfolio-orders
alias_name=portfolio-orders

broker() {
    local tool=$1
    shift
    "${podman[@]}" exec -i "$kafka" "/opt/kafka/bin/kafka-$tool.sh" --bootstrap-server 127.0.0.1:9092 "$@"
}

search_request() {
    local method=$1
    local route=$2
    shift 2
    "${podman[@]}" exec -i "$search" curl --noproxy '*' --fail-with-body --silent --show-error \
        --max-time 10 -X "$method" -H 'Content-Type: application/json' "$@" "http://127.0.0.1:9200$route"
}

consume() {
    broker console-consumer --topic "$topic" --group "$1" --from-beginning \
        --command-property enable.auto.commit=false --max-messages 3 --timeout-ms 20000 > "$2"
    cmp -- out/installed/orders.ndjson "$2"
}

index_events() {
    local index=$1
    local projection=$2
    local record id
    while IFS= read -r record; do
        id=$(jq -er '.id | select(test("^[a-z0-9-]+$"))' <<< "$record")
        jq -c "$projection" <<< "$record" | search_request PUT "/$index/_doc/$id" --data-binary @- |
            jq -e --arg id "$id" '._id == $id and ._shards.failed == 0' > /dev/null
    done < "$3"
    search_request POST "/$index/_refresh" | jq -e '._shards.failed == 0' > /dev/null
}

count_documents() {
    search_request GET "/$1/_count" | jq -e '.count == 2 and ._shards.failed == 0' > /dev/null
}

search_ready() {
    search_request GET '/_cluster/health?wait_for_status=yellow&timeout=1s' |
        jq -e '.timed_out == false and (.status == "green" or .status == "yellow")'
}

broker_ready() {
    timeout --kill-after=2s 4s "${podman[@]}" exec "$kafka" bash -c 'exec 3<>/dev/tcp/127.0.0.1/9092'
}

case "$stage" in
    build)
        jq -es 'length == 3 and ([.[].id] | unique | length == 2) and
            all(.[]; (.id | test("^[a-z0-9-]+$")) and (.cents >= 0) and (.market == "east" or .market == "west"))' \
            fixture/orders.ndjson > /dev/null
        mkdir -p out/distribution
        cp -- fixture/orders.ndjson fixture/v1.json fixture/v2.json out/distribution/
        pack out/distribution
        ;;
    install)
        install_package
        start_target "$kafka"
        start_target "$search"
        wait_for 50 broker_ready
        wait_for 80 search_ready
        ;;
    migrate)
        jq -e --arg kafka "$kafka" --arg search "$search" \
            '.kafka.container == $kafka and .search.container == $search' out/config.json > /dev/null
        broker topics --create --topic "$topic" --partitions 1 --replication-factor 1
        broker console-producer --topic "$topic" --command-property acks=all < out/installed/orders.ndjson
        broker get-offsets --topic "$topic" > out/offsets.txt
        grep -Fxq -- "$topic:0:3" out/offsets.txt

        consume portfolio-baseline out/first-read.ndjson
        search_request PUT /orders-v1 --data-binary @- < out/installed/v1.json |
            jq -e '.acknowledged' > /dev/null
        index_events orders-v1 '.' out/first-read.ndjson
        count_documents orders-v1
        printf '%s' '{"actions":[{"add":{"index":"orders-v1","alias":"portfolio-orders"}}]}' |
            search_request POST /_aliases --data-binary @- | jq -e '.acknowledged' > /dev/null

        consume portfolio-rebuild out/replay.ndjson
        search_request PUT /orders-v2 --data-binary @- < out/installed/v2.json |
            jq -e '.acknowledged' > /dev/null
        index_events orders-v2 '{id,title,cents,region:.market}' out/replay.ndjson
        count_documents orders-v2
        search_request GET /orders-v2/_doc/o-101 |
            jq -e '.found and ._source.region == "east" and ._source.cents == 1250' > /dev/null
        printf '%s' '{"actions":[{"remove":{"index":"orders-v1","alias":"portfolio-orders"}},{"add":{"index":"orders-v2","alias":"portfolio-orders"}}]}' |
            search_request POST /_aliases --data-binary @- | jq -e '.acknowledged' > /dev/null
        jq -n --rawfile offsets out/offsets.txt \
            --arg replaySha256 "$(sha256sum out/replay.ndjson | cut -d ' ' -f1)" \
            '{offsets:($offsets | rtrimstr("\n")), replaySha256:$replaySha256,
              previousIndex:"orders-v1", activeIndex:"orders-v2"}' > out/migration.json
        ;;
    verify)
        count_documents "$alias_name"
        count_documents orders-v1
        search_request GET "/_alias/$alias_name" > out/alias.json
        jq -e 'keys == ["orders-v2"]' out/alias.json > /dev/null
        printf '%s' '{"query":{"bool":{"must":[{"match":{"title":"pencil"}}],"filter":[{"term":{"region":"east"}}]}}}' |
            search_request POST "/$alias_name/_search" --data-binary @- > out/query.json
        jq -e '._shards.failed == 0 and .hits.total.value == 1 and
               .hits.hits[0]._id == "o-101" and .hits.hits[0]._source.cents == 1250' out/query.json > /dev/null
        jq -n --slurpfile mapping out/alias.json --slurpfile response out/query.json \
            '{alias:$mapping[0],hits:$response[0].hits.hits}' > out/verification.json
        ;;
    *) exit 64 ;;
esac
