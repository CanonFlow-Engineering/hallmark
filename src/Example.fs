namespace Hallmark.Lab

open Hallmark.Lab.Serialization

module Example =
    let sixStagePlan =
        let stage id kind effect repetition dependencies = {
            Id = id
            Kind = kind
            Effect = effect
            Repetition = repetition
            AdapterDigest = ContentDigest.ofText ("fixture-adapter:" + id)
            ActionDigest = ContentDigest.ofText ("fixture-action:" + id)
            Inputs = [{ Name = "fixture-input"; Digest = ContentDigest.ofText ("fixture-input:" + id) }]
            DependsOn = dependencies
        }
        {
            SchemaVersion = 1
            Name = "six-stage-service"
            ProducerDigest = ContentDigest.ofText "hallmark-lab-v1"
            TargetDigest = ContentDigest.ofText "synthetic-local-target-v1"
            ConfigurationDigest = ContentDigest.ofText "synthetic-configuration-v1"
            PolicyDigest = ContentDigest.ofText "synthetic-policy-v1"
            Stages = [
                stage "build" Build Imperative ExecuteOnce []
                stage "install" Install DesiredState Reconcile ["build"]
                stage "configure" Configure DesiredState Reconcile ["install"]
                stage "migrate" Migrate Imperative ExecuteOnce ["configure"]
                stage "verify" Verify Observation Reconcile ["migrate"]
                stage "release" Release Imperative ExecuteOnce ["verify"]
            ]
        }
