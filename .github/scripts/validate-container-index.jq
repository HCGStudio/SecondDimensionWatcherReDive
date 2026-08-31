def is_sha256:
  type == "string" and test("^sha256:[0-9a-f]{64}$");

def is_positive_integer:
  type == "number" and . > 0 and floor == .;

def is_runnable_platform:
  . == {"architecture": "amd64", "os": "linux"} or
  . == {"architecture": "arm64", "os": "linux"};

def is_attestation_platform:
  . == {"architecture": "unknown", "os": "unknown"};

. as $index
| ($index.manifests // []) as $manifests
| [$manifests[] | select(.platform | is_runnable_platform)] as $runnable
| [$manifests[] | select(.platform | is_attestation_platform)] as $attestations
| $index.schemaVersion == 2
  and $index.mediaType == "application/vnd.oci.image.index.v1+json"
  and ($index.manifests | type == "array")
  and ($runnable | map(.platform) | sort_by(.architecture)) == [
    {"architecture": "amd64", "os": "linux"},
    {"architecture": "arm64", "os": "linux"}
  ]
  and ($runnable | map(.digest) | unique | length) == 2
  and ($attestations | length) == 2
  and ($manifests | length) == 4
  and all($manifests[];
    .mediaType == "application/vnd.oci.image.manifest.v1+json"
    and (.digest | is_sha256)
    and (.size | is_positive_integer)
    and (.platform | is_runnable_platform or is_attestation_platform)
  )
  and all($attestations[];
    (.annotations | type == "object")
    and .annotations["vnd.docker.reference.type"] == "attestation-manifest"
    and (.annotations["vnd.docker.reference.digest"] | is_sha256)
  )
  and (
    $attestations | map(.annotations["vnd.docker.reference.digest"]) | sort
  ) == (
    $runnable | map(.digest) | sort
  )
