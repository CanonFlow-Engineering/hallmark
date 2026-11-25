#!/usr/bin/env bash
set -euo pipefail
source fixture/common.sh

duckdb=$(jq -er '.duckdb.executable' target.json)
expected=$(jq -er '.duckdb.sha256' target.json)
[[ $(sha256sum "$duckdb" | cut -d ' ' -f1) == "$expected" ]]

db=out/sales.duckdb
runtime=("$duckdb" -no-agent -init /dev/null -batch -bail
    -cmd "SET autoinstall_known_extensions=false; SET autoload_known_extensions=false; SET threads=2; SET memory_limit='256MB';")

case "$stage" in
    build)
        mkdir -p out/distribution
        cp -- fixture/V1.sql fixture/V2.sql out/distribution/
        pack out/distribution
        ;;
    install)
        install_package
        if [[ ! -e $db ]]; then
            "${runtime[@]}" out/staged.duckdb < out/installed/V1.sql
            mv -T -- out/staged.duckdb "$db"
        fi
        ;;
    migrate)
        jq -e '.duckdb.version | startswith("v2.0.0 ")' out/config.json > /dev/null
        "${runtime[@]}" -readonly -json "$db" \
            'SELECT count(*)::INTEGER AS sales, sum(amount)::DOUBLE AS total FROM sales' > out/before.json
        jq -e '.[0] == {"sales":3,"total":72.5}' out/before.json > /dev/null
        "${runtime[@]}" "$db" < out/installed/V2.sql
        jq -n --slurpfile before out/before.json \
            --arg parquetSha256 "$(sha256sum out/region_totals.parquet | cut -d ' ' -f1)" \
            '{before:$before[0][0], parquetSha256:$parquetSha256}' > out/migration.json
        ;;
    verify)
        "${runtime[@]}" -readonly -json "$db" \
            'SELECT sale_id, region, amount_cents FROM sales ORDER BY sale_id' > out/rows.json
        "${runtime[@]}" -readonly -json "$db" \
            "SELECT * FROM read_parquet('out/region_totals.parquet') ORDER BY region" > out/parquet.json
        jq -e '. == [{"sale_id":"0001","region":"east","amount_cents":1250},
                     {"sale_id":"0002","region":"east","amount_cents":2000},
                     {"sale_id":"0003","region":"west","amount_cents":4000}]' out/rows.json > /dev/null
        jq -e '. == [{"region":"east","sales":2,"cents":3250},
                     {"region":"west","sales":1,"cents":4000}]' out/parquet.json > /dev/null
        jq -n --slurpfile rows out/rows.json --slurpfile parquet out/parquet.json \
            '{sales:$rows[0],parquet:$parquet[0]}' > out/verification.json
        ;;
    *) exit 64 ;;
esac
