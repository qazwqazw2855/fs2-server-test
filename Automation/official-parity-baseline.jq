{
  schema: "God2OfficialParityBaseline/1",
  generatedFromProgressAt: $progress[0].updated_at,
  policy: $registry[0].policy,
  sources: {
    registry: "docs/parity/official-parity-registry.json",
    runtimeMappings: "protocol/evidence/current-build/runtime-mappings.json",
    classificationGates: "protocol/evidence/current-build/classification-gates.json",
    liveClassification: "protocol/evidence/current-build/live-classification.json",
    mapBaseline: "docs/parity/map.json",
    progress: "progress.json"
  },
  mapBaseline: {
    clientBuildId: $mapBaseline[0].clientBuildId,
    inventory: $mapBaseline[0].inventory,
    diff: $mapBaseline[0].diff,
    provenanceLinks: $mapBaseline[0].provenanceLinks,
    promotionGate: $mapBaseline[0].promotionGate,
    authorityBoundary: $mapBaseline[0].authorityBoundary
  },
  protocolEvidenceSets: {
    runtimeMappings: {
      count: ($mappings[0] | length),
      uniqueFamilies: ([$mappings[0][].family] | unique | length),
      runtimeMutationBlocked: ([$mappings[0][] | select(.status == "RuntimeMutationBlocked")] | length),
      runtimeIntegrated: ([$mappings[0][] | select(.runtimeIntegrated == true)] | length)
    },
    evidenceRoutes: {
      count: ($classification[0].EvidenceRoutes | length),
      candidateGateCount: $gates[0].RuntimeMutationBlockedCount
    },
    liveClassification: {
      clientBuildId: $classification[0].ClientBuildId,
      generatedAtUtc: $classification[0].GeneratedAtUtc,
      totalFrames: $classification[0].Counters.TotalFrames,
      knownVerified: $classification[0].Counters.KnownVerified,
      knownCandidate: $classification[0].Counters.KnownCandidate,
      unknownNew: $classification[0].Counters.UnknownNew,
      unknownActionable: ($classification[0].UnknownActionable | length),
      decoderVerifiedChanges: $classification[0].Counters.DecoderVerifiedChanges,
      serializerVerifiedChanges: $classification[0].Counters.SerializerVerifiedChanges
    },
    boundary: "The 91 runtime mappings, 97 evidence routes and 17 unknown-new classifications are separate sets and must not be merged."
  },
  projectProgress: {
    overallPercent: $progress[0].overall_percent,
    phase: $progress[0].phase,
    currentFocus: $progress[0].current_focus
  },
  systems: ($registry[0].systems | sort_by(.priority, .id))
}
