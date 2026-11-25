def process($id; $kind; $effect; $needs; $outputs; $timeout; $env):
  {
    id: $id, kind: $kind, effect: $effect, needs: $needs, outputs: $outputs,
    repeat: (if $effect == "Imperative" then "ExecuteOnce" else "Reconcile" end),
    retrySafety: (if $effect == "Imperative" then "manual" else "reconcile" end),
    timeoutSeconds: $timeout,
    operation: {
      type: "process", executable: "/bin/bash",
      arguments: ["fixture/run.sh", $id], environment: $env
    }
  };

def configuration:
  {
    id: "configure", kind: "Configure", effect: "DesiredState",
    repeat: "Reconcile", retrySafety: "reconcile", needs: ["install"],
    operation: {type: "write-file", path: "out/config.json", content: ($target | tojson)}
  };

def postgres:
  [
    process("baseline"; "Migrate"; "Imperative"; ["configure"]; ["out/before.json"]; 120;
      {DATABASE_PASSWORD: $env.DATABASE_PASSWORD}),
    {
      id: "migrate", kind: "Migrate", effect: "Imperative",
      repeat: "ExecuteOnce", retrySafety: "external-ledger", needs: ["baseline"],
      timeoutSeconds: 120, outputs: ["out/migration.json"],
      operation: {
        type: "postgres-migrate", containerEngine: "podman",
        containerId: $target.database.container, database: $target.database.name, user: "fixture",
        passwordSecret: "database-password",
        migrations: ["fixture/V1__baseline.sql", "fixture/V2__upgrade.sql"]
      }
    },
    process("exercise"; "Install"; "Imperative"; ["migrate"]; ["out/exercise.json"]; 180; $env)
  ];

($target + {sample: $sample}) as $identity |
{
  schemaVersion: 1, name: $sample, context: $sample,
  inputs: (["fixture", "target.json"] + $inputs),
  allowedExecutables: ["/bin/bash"],
  steps: ([
    process("build"; "Build"; "Imperative"; []; ["out/distribution.tar.gz", "out/build.json"]; 600; {}),
    process("install"; "Install"; "DesiredState";
      ["build"]; ["out/install.json"]; 180; {}),
    (configuration | .operation.content = ($identity | tojson))
  ] + (
    if $target.database then postgres
    else [
      process("migrate"; "Migrate"; "Imperative"; ["configure"];
        (["out/migration.json"] + if $sample == "duckdb" then ["out/region_totals.parquet"] else [] end); 240; {})
    ]
    end
  ) + [
    process("verify"; "Verify"; "Observation";
      [if $target.database then "exercise" else "migrate" end]; ["out/verification.json"]; 150; {}),
    {
      id: "release", kind: "Release", effect: "Imperative", repeat: "ExecuteOnce",
      needs: ["verify"], operation: {type: "release", version: "0.2.0"},
      retrySafety: "external-ledger"
    }
  ])
}
