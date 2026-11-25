# Sourced by the portfolio harness. Each request uses a local MCP stdio session.
mcp_call() {
    local label=$1 tool=$2 arguments=$3
    jq -cn --arg tool "$tool" --argjson arguments "$arguments" '
      {jsonrpc:"2.0", id:1, method:"initialize", params:{
          protocolVersion:"2025-11-25", capabilities:{},
          clientInfo:{name:"hallmark-portfolio", version:"0.2.0"}}},
      {jsonrpc:"2.0", method:"notifications/initialized"},
      {jsonrpc:"2.0", id:2, method:"tools/call", params:{name:$tool,arguments:$arguments}}
      ' > "$evidence/$label.request.ndjson"
    record "$label" env -u HALLMARK_MCP_ALLOW_EFFECTS timeout --kill-after=5s 60s \
        "$runner" mcp --state-dir "$state" < "$evidence/$label.request.ndjson"
    jq -es '
        any(.[]; .id == 1 and .result.serverInfo.name == "hallmark-mcp") as $initialized |
        [.[] | select(.id == 2 and .result.structuredContent != null)] |
        select($initialized and length == 1) | .[0].result
        ' "$evidence/$label.out" > "$evidence/$label.result.json"
}

mcp_preview() {
    local args
    record preview-freeze "${hm[@]}" freeze "$PWD/workflow.json" "$evidence/preview-frozen.json" --state-dir "$state"
    args=$(jq -cn --arg manifest "$PWD/workflow.json" '{manifest:$manifest}')
    mcp_call mcp-plan hallmark_plan "$args"
    jq -e --slurpfile cli "$evidence/plan.out" --slurpfile frozen "$evidence/preview-frozen.json" '
        .isError == false and
        (.structuredContent |
            .planDigest == $cli[0].planDigest and
            .stages == $cli[0].stages and
            .planDigest == $frozen[0].planDigest and
            .sourceDigest == $frozen[0].sourceDigest and
            .policyDigest == $frozen[0].policyDigest and
            .targetDigest == $frozen[0].targetDigest)
        ' "$evidence/mcp-plan.result.json" > /dev/null
}

prepare_wallet_gate() {
    local fixture="$repo/examples/three-owner/fixture.json"
    local held="$evidence/owner-withdrawal.json"
    local correction=PORTFOLIO-WALLET-WITHDRAWAL
    local source="$PORTFOLIO_FSHARP"
    local revision tree policy_sha
    revision=$(jq -er '.revision' target.json)
    tree=$(git -C "$source" rev-parse "$revision^{tree}")
    policy_sha=$(git -C "$source" show "$revision:fsassay-policy.lock.json" | sha256sum | cut -d ' ' -f1)
    jq --arg correction "$correction" --arg when "$(date -u +%Y-%m-%dT%H:%M:%SZ)" '
        (.receipts[] | select(.packageId == "wallet-search-os")) as $prior |
        .releaseVersion = "0.2.0" |
        .changeSets[0].migrationVersion = "V2__upgrade" |
        .changeSets[0].summary = "Add operations_wallet_idx on the Wallet operations table" |
        .corrections += [{correctionId:$correction,affectedReceiptDigest:$prior.receiptDigest,
            actor:$prior.ownerTeam,reason:"Simulate withdrawal of the search compatibility decision",
            effectiveAt:$when,kind:"Withdrawn"}]
        ' "$fixture" > "$held"
    jq -n --arg lineage "Withdrawn:$correction" --arg ownerFixture "$held" \
        --arg revision "$revision" --arg tree "$tree" --arg policySha "$policy_sha" \
        '{requiredAssays:["FsAssay"],
          expectedProducers:[{assay:"FsAssay",tool:"FsAssay",toolVersion:"1.0.4",commitSha:$revision,treeSha:$tree,policySha256:$policySha}],
          lineage:$lineage,threeOwnerFixture:$ownerFixture,receipts:[]}' \
        > "$evidence/held-gate.json"
    cp -- "$evidence/held-gate.json" gate-b-policy.json
}

mcp_review_bundle() {
    local bundle=$1 key=$2 run=$3 digest=$4
    local args
    args=$(jq -cn --arg path "$bundle" --arg key "$key" '{bundlePath:$path,publicKeyPath:$key}')
    mcp_call mcp-bundle hallmark_bundle_verify "$args"
    jq -e --arg run "$run" --arg digest "$digest" --slurpfile native "$evidence/independent-verify.out" '
        .isError == false and
        (.structuredContent | .valid and .runId == $run and .planDigest == $digest and
            .artifactCount == $native[0].artifactCount)
        ' "$evidence/mcp-bundle.result.json" > /dev/null
}

verify_wallet_source() (
    local source="$PORTFOLIO_FSHARP"
    local proof="$evidence/wallet-assay"
    local solution=FSharpAssay.Playground.slnx
    local revision dotnet packages producer tool container mapped
    revision=$(jq -er '.revision' target.json)
    dotnet=$(jq -er '.toolchain.dotnet' target.json)
    packages=$(jq -er '.toolchain.packages' target.json)
    container=$(jq -er '.database.container' target.json)
    producer="$source/.packages/producer.json"
    tool="$source/$(jq -er '.tool' "$producer")"
    [[ $(sha256sum "$tool" | cut -d ' ' -f1) == "$(jq -er '.toolSha256' "$producer")" ]]
    mkdir -- "$proof"
    cp -- "$producer" "$proof/producer.json"
    record wallet-assay-clone git clone --shared --no-checkout -- "$source" "$proof/source"
    record wallet-assay-checkout git -C "$proof/source" checkout --detach "$revision"
    record wallet-assay-database "${podman[@]}" exec "$container" psql -XwqtA -v ON_ERROR_STOP=1 \
        -U fixture -d fsplayground_portfolio -c 'CREATE DATABASE fsplayground_portfolio_assay'
    mapped=$("${podman[@]}" port "$container" 5432/tcp)
    [[ $mapped =~ ^127[.]0[.]0[.]1:([0-9]+)$ ]]
    export FSPLAYGROUND_POSTGRES="Host=127.0.0.1;Port=${BASH_REMATCH[1]};Database=fsplayground_portfolio_assay;Username=fixture;Password=$wallet_assay_password;SSL Mode=Disable;Timeout=5;Command Timeout=5"
    export DOTNET_ROOT="$(dirname -- "$dotnet")"
    export PATH="$DOTNET_ROOT:$PATH" NUGET_PACKAGES="$packages"
    unset DOTNET_ROLL_FORWARD
    cd -- "$proof/source"
    git rev-parse HEAD 'HEAD^{tree}' > "$proof/source.txt"
    sha256sum -- "$dotnet" "$tool" .fsassayrc fsassay-policy.lock.json global.json > "$proof/inputs.sha256"
    record wallet-assay-restore timeout --kill-after=10s 180s "$dotnet" restore "$solution" --locked-mode --packages "$packages"
    record wallet-assay-build timeout --kill-after=10s 180s "$dotnet" build "$solution" -c Release --no-restore --disable-build-servers -m:4
    record wallet-fsassay timeout --kill-after=15s 400s "$tool" verify \
        --out-json "$proof/fsassay.json" --assessment-json "$proof/assessment.json" \
        --test-artifacts "$proof/registered-tests" "$solution"
    jq -e --arg revision "$revision" '
        .outcome == "Pass" and .authoritative and .candidate.commitSha == $revision and
        any(.tests[]; .id == "real-wallet" and .passed >= 22 and .failed == 0 and .skipped == 0)
        ' "$proof/fsassay.json" > /dev/null
)

wallet_hold_and_resume() {
    local digest=$1
    local fixture="$repo/examples/three-owner/fixture.json"
    local held="$evidence/owner-withdrawal.json"
    local corrected="$evidence/owner-replacement.json"
    local correction=PORTFOLIO-WALLET-WITHDRAWAL
    local args id container count
    args=$(jq -cn --arg manifest "$PWD/workflow.json" --arg digest "$digest" '{manifest:$manifest,planDigest:$digest}')
    mcp_call mcp-apply-refused hallmark_apply "$args"
    jq -e '.isError and .structuredContent.code == "MCP_EFFECTS_DISABLED"' \
        "$evidence/mcp-apply-refused.result.json" > /dev/null

    # These are synthetic owner decisions. PostgreSQL and its Flyway ledger
    # are the real Wallet deployment provisioned for this run.
    record_expected owner-held 3 "${hm[@]}" three-owner-check "$held"
    jq -e '.admitted == false and any(.handoffs[]; .consumingPackageId == "wallet-search-os" and
        .status == "correction-required" and (.dependencyPaths | length) == 2)' "$evidence/owner-held.out" > /dev/null

    cp -- "$evidence/held-gate.json" gate-b-policy.json
    record_expected apply 1 "${hm[@]}" apply "$PWD/workflow.json" --plan-digest "$digest" --state-dir "$state"
    id=$(jq -er '.RunId' "$evidence/apply.out")
    jq -e '.Status == "failed" and .Steps[-1].Id == "release" and
        (.Steps[-1].Message | contains("Gate B admission refused release")) and
        all(.Steps[0:-1][]; .Status == "succeeded")' "$evidence/apply.out" > /dev/null
    [[ ! -e "$state/$sample/releases/0.2.0.json" ]]

    record triage "${hm[@]}" triage --context "$sample" --fixture "$held" --state-dir "$state"
    jq -e --arg id "$id" --arg correction "$correction" '
        .runId == $id and .runStatus == "failed" and
        any(.committedEffects[]; .stepId == "migrate" and .version == "2" and .status == "committed") and
        .responsibleBlockerOwners == ["opensearch-projection-team"] and
        any(.ownerBlockers[]; contains($correction)) and
        any(.unresolvedSteps[]; .stepId == "release") and
        (.safeNextAction | contains("hallmark engine resume"))
        ' "$evidence/triage.out" > /dev/null
    args=$(jq -cn --arg context "$sample" --arg fixture "$held" '{context:$context,fixturePath:$fixture}')
    mcp_call mcp-triage hallmark_triage "$args"
    jq -e --slurpfile cli "$evidence/triage.out" '.isError == false and .structuredContent == $cli[0]' \
        "$evidence/mcp-triage.result.json" > /dev/null

    # A replacement fixture receipt keeps the original withdrawal in the owner graph.
    local replacement
    replacement=$(printf '%s' 'portfolio synthetic search compatibility replacement' | sha256sum | cut -d ' ' -f1)
    jq --arg digest "sha256:$replacement" '
        .receipts += [(.receipts[] | select(.packageId == "wallet-search-os") | .receiptDigest = $digest)]
        ' "$held" > "$corrected"
    record owner-corrected "${hm[@]}" three-owner-check "$corrected"
    jq -e '.admitted and all(.handoffs[]; .status == "satisfied")' "$evidence/owner-corrected.out" > /dev/null

    verify_wallet_source
    record freeze "${hm[@]}" freeze "$PWD/workflow.json" "$evidence/frozen-workflow.json" --state-dir "$state"
    jq -e --arg digest "$digest" '.planDigest == $digest' "$evidence/freeze.out" > /dev/null
    local native_receipt
    native_receipt=$(sha256sum "$evidence/wallet-assay/fsassay.json" | cut -d ' ' -f1)
    jq --arg receipt "sha256:$native_receipt" --arg receiptPath "$evidence/wallet-assay/fsassay.json" --arg ownerFixture "$corrected" \
        --slurpfile heldGate "$evidence/held-gate.json" '
        {requiredAssays:["FsAssay"],
         expectedProducers:$heldGate[0].expectedProducers,
         lineage:"Active",threeOwnerFixture:$ownerFixture,receipts:[
            {assay:"FsAssay",outcome:"Pass",receiptDigest:$receipt,receiptPath:$receiptPath,planDigest,sourceDigest,policyDigest,targetDigest}
        ]}' "$evidence/frozen-workflow.json" > "$evidence/active-gate.json"
    cp -- "$evidence/active-gate.json" gate-b-policy.json
    record resume "${hm[@]}" resume "$PWD/workflow.json" --run "$id" --plan-digest "$digest" --state-dir "$state"
    jq -e --arg id "$id" --slurpfile held "$evidence/apply.out" '
        .Status == "succeeded" and .RunId == $id and .PlanDigest == $held[0].PlanDigest and
        .Steps[0:-1] == $held[0].Steps[0:-1] and .Steps[-1].Id == "release"
        ' "$evidence/resume.out" > /dev/null

    # Post-seal approval withdrawal refuses promotion and convergence until re-authorized.
    local post_seal_key="$evidence/post-seal-public.pem"
    record post-seal-key "${hm[@]}" public-key --context "$sample" --state-dir "$state"
    cp -- "$evidence/post-seal-key.out" "$post_seal_key"
    cp -- "$held" "$corrected"
    record_expected promote-withdrawn 3 "${hm[@]}" promote 0.2.0 --channel prod --context "$sample" --public-key "$post_seal_key" --state-dir "$state"
    [[ ! -e "$state/$sample/channels/prod.json" ]]
    record_expected converge-withdrawn 3 "${hm[@]}" converge "$PWD/workflow.json" --run "$id" --state-dir "$state"
    jq --arg digest "sha256:$replacement" '
        .receipts += [(.receipts[] | select(.packageId == "wallet-search-os") | .receiptDigest = $digest)]
        ' "$held" > "$corrected"
    record promote-admitted "${hm[@]}" promote 0.2.0 --channel prod --context "$sample" --public-key "$post_seal_key" --state-dir "$state"
    [[ -s "$state/$sample/channels/prod.json" ]]

    jq -es --arg id "$id" '[.[] | select(.RunId == $id and .Stage == "migrate" and .Status == "running")] | length == 1' \
        "$state/$sample/history.ndjson" > /dev/null
    container=$(jq -er '.database.container' target.json)
    record flyway-v2 "${podman[@]}" exec "$container" psql -XwqtA -v ON_ERROR_STOP=1 -U fixture \
        -d fsplayground_portfolio -c "SELECT count(*) FROM public.flyway_schema_history WHERE version='2' AND success"
    read -r count < "$evidence/flyway-v2.out"
    [[ $count == 1 ]]
}
