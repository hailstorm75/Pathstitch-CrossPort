# ADR-001: Restore STEP/B-rep support through a packaged OCCT worker

- Status: Accepted; implementation amended 2026-07-18
- Date: 2026-07-10
- Deciders: Pathstitch maintainers
- Backlog: AUD-015; unblocks AUD-016 and AUD-017

## Context

Pathstitch needs more than a rendered STEP mesh. The 3D-to-2D workflow depends on stable bodies, faces, edges and adjacency; recognition of planes, cylinders and cones; exact curves and parameter-space curves; sections and hidden-line projection; and developable-surface unfolding. OBJ/STL conversion discards those properties and is therefore not STEP parity.

The repository currently has three relevant paths:

1. The active Avalonia editor calls the .NET `IEditor3DOperationService` contract in `src/Domain/Domain.App/Services/IEditor3DOperationService.cs`. Its implementation, `src/Pathstitch.App/Services/OpenGeometryEditor3DOperationService.cs`, now routes STEP/STP/OBJ/STL through the packaged OCCT worker in production. Mixed imports normalize pairwise to a retained STEP document. The Node/WASM OpenGeometry bridge remains useful for 2D operations and the explicit null-kernel/legacy-JSON mesh fallback.
2. The original macOS application keeps one persistent length-prefixed JSON worker (`Pathstitch/Pathstitch/Bridge/PythonBridge.swift`). `pathstitch_core/step_ops.py`, `surface_unfold.py`, and `net_unfold.py` use pythonOCC/OpenCASCADE for STEP transfer, body/face/edge traversal, analytic surface recognition, exact edge and p-curve access, sections/HLR, and planar/cylindrical/conical plus mesh-based unfolding. `scripts/package_app.sh` proves an Apple-silicon Python 3.11/OCC environment can be bundled, although the documented trimmed environment is still about 1.1 GB and is not a deterministic cross-platform Avalonia package.
3. `native/step_mesh` wraps the pinned MIT/Apache-2.0 foxtrot tessellator for macOS Quick Look. Its contract returns triangles only. It remains appropriate for previews, not editor STEP topology or exact geometry.

The repository is GPL-3.0. Even so, dependency notices, source/offer obligations, dynamic-linking terms, and platform redistributability must be reviewed before release; this ADR is an engineering decision, not legal advice.

## 2026-07-18 implementation amendment

The packaged worker contract is still kernel-neutral, but its accepted source scope now includes OBJ and STL as well as STEP. The existing Python loader already supported all three formats and pairwise mixed-format combine; C# dispatch had prevented packaged mesh imports from reaching it. PAR-009 unified routing and added `obj-import`, `stl-import`, and `mixed-combine` capabilities.

Mesh normalization also required two topology repairs: OBJ faces are sewn before extraction, and STEP reload sewing retains free mesh shells beside solids after mixed-format export. Packaged tests prove stable two-triangle OBJ/STL topology, connected-net folds and manual cuts, decoration layers, projection, and STEP + OBJ + STL reimport. Legacy JSON mesh workspaces remain on the reduced fallback and cannot be combined with STEP.
## Decision drivers

- Preserve B-rep topology, analytic surface classification and exact boundary curves through import, selection, projection, unfold, save and reopen.
- Match or improve the proven legacy results before removing that implementation.
- Keep the Domain and UI layers independent of a particular kernel and process runtime.
- Ship offline, reproducibly, on Windows and macOS without requiring users to install Python, Conda, Node, or a CAD SDK.
- Isolate native crashes and long-running kernel work from the UI process.
- Permit a later kernel replacement without another editor rewrite.

## Considered options

### A. Embed a cross-platform OCCT .NET binding

A native OCCT binding could expose the richest in-process model and avoid serialization. OCCT supplies STEP transfer, B-rep topology, analytic surfaces, exact curves, section/HLR algorithms and tessellation. OCCT is distributed under LGPL-2.1 with the OCCT exception; the selected binding can impose additional terms that must be audited.

The drawbacks are decisive today. A binding must have maintained .NET 10 support, complete wrappers for the algorithms above, matching native binaries for every RID/architecture, safe ownership/lifetime semantics, and a documented redistribution story. The previously removed `Occt.NET` route and the absence of such artifacts in the active project leave those claims unproven. In-process native faults would also terminate the editor. This option may be reconsidered after a binding passes the gates below, but it is not the restoration path.

### B. Package a versioned geometry worker/backend built on OCCT (selected)

The Avalonia application will talk to a long-lived, out-of-process geometry service through a versioned, kernel-neutral contract. The first backend will adapt the existing pythonOCC/OpenCASCADE implementation because it is the repository's working behavioral reference. It is packaged per runtime identifier and treated as an implementation detail: no Python or OCC types cross the process or Domain boundary.

This choice combines a stable .NET seam with the shortest route to exact STEP parity. Process isolation contains native crashes and leaks, supports cancellation/timeout/restart, and permits a later native OCCT or OpenGeometry backend behind the same conformance suite. The cost is IPC, explicit handle/session management, and substantial packaging work. Those costs are preferable to losing topology or rewriting proven unfold algorithms during the port.

“Packaged worker” is the durable architecture; “Python/pythonOCC” is the initial backend, not a permanent UI dependency.

### C. Retain the legacy Python worker as-is

Directly reusing `pathstitch_core.worker` has the lowest initial code cost and preserves current algorithms. It also retains a protocol spanning unrelated 2D and 3D modules, filesystem-shaped results (including generated DXF), platform-specific interpreter discovery, a large Conda-derived bundle, and Apple-silicon assumptions. Calling its existing operation dictionaries directly from Avalonia would cement legacy details and make testing, version negotiation, cancellation, packaging and replacement harder.

We will reuse its tested geometry code behind an adapter, but not adopt its current Swift-facing protocol or deployment layout as the new application contract.

## Contract and fidelity requirements

The worker protocol must be documented and versioned before STEP is enabled. A request has an ID, protocol version, operation, input/session reference, tolerances and cancellation context. A response has the same ID, backend/version metadata, typed success data or a structured error. Transport may initially use length-prefixed JSON over standard streams, as the legacy worker already validates that pattern; transport is not part of Domain models.

The import result must include:

- stable document-scoped body, face, wire and edge IDs (never array position as persisted identity);
- body/shell/face/wire/edge topology, orientation and edge-to-face adjacency;
- face surface kind and parameters at least for plane, cylinder and cone, with an explicit `other`/spline category;
- exact 3D edge curve type and parameters plus per-face p-curve information needed by unfolding;
- tessellation as a derived viewport representation mapped back to body/face IDs;
- source units, import diagnostics and the tolerances used.

Projection and unfold operations must consume stable IDs and return typed 2D curves (line, arc/circle and spline where supported), loops and provenance. Polyline sampling is allowed as an explicitly requested display/export approximation, never as the canonical imported STEP representation. Project/session persistence must embed the original STEP bytes or a lossless backend-neutral B-rep payload plus contract version and ID mapping; cached tessellation alone is insufficient.

## Packaging and licensing constraints

- Produce locked, repeatable worker bundles for each supported RID/architecture; do not run Conda resolution or npm installation on an end-user machine.
- Bundle the interpreter, pythonOCC, OCCT shared libraries and only required Python modules in an app-owned directory. The app must resolve the worker relative to its installation, never from PATH or a developer-specific location.
- Generate a software bill of materials and third-party notices from the exact lock/build inputs. Preserve the repository's GPL-3.0 obligations and review pythonOCC, OCCT, runtime and transitive native licenses before distribution.
- On macOS, build and test both target architectures (or explicitly publish architecture-specific artifacts), fix dylib install names/rpaths, include the worker and libraries in code signing, enable hardened runtime, notarize the final app, and verify execution from a quarantined `.app` without Homebrew/Conda. The worker remains outside the sandboxed Quick Look extension; `native/step_mesh` continues to serve preview-only needs.
- On Windows, ship the matching worker executable/runtime and OCCT DLL closure beside the app, and verify operation on a clean VM without Python, Node or Visual C++ components beyond those included by the installer.

OpenGeometry remains the current backend for supported 2D operations and the reduced legacy mesh fallback. Packaged releases normalize STEP/STP/OBJ/STL through OCCT so every 3D source format uses one stable-topology projection/unfold contract.

## Migration plan

1. Define kernel-neutral import/projection/unfold DTOs and a backend capability/version handshake adjacent to `IEditor3DOperationService`; keep UI/ViewModels unaware of process and Python details.
2. Add contract tests and representative STEP fixtures before changing import behavior. Capture legacy pythonOCC results and tolerance envelopes as the reference.
3. Add a new worker entry point/adapter that exposes only the versioned 3D contract and delegates to the existing `step_ops`, `surface_unfold`, and `net_unfold` algorithms. Add timeout, cancellation, progress, crash restart and stderr diagnostics.
4. Replace filesystem-only DXF responses with typed geometry while retaining a temporary adapter for comparison. Introduce stable topology IDs and map viewport triangles back to them.
5. Build locked worker artifacts for Windows x64 and supported macOS architectures. Add clean-machine packaging tests, SBOM/notices, signing and notarization checks.
6. Add a backend selector/feature flag. Run the packaged worker and legacy reference against the parity suite; use OpenGeometry only for capabilities it declares.
7. Enable the packaged backend for STEP/STP/OBJ/STL and mixed imports after the mandatory gates pass. Retain legacy JSON mesh workspaces only as an explicit reduced fallback.
8. After two release cycles with telemetry-free local diagnostic logs showing no systemic regression, remove the temporary legacy protocol adapter. Retain fixtures and contract tests permanently.

## Mandatory test gates

AUD-016 cannot be considered complete until all of these pass on every supported release platform:

1. **Import topology:** box, cylinder, cone, multi-body assembly-like file, holes/inner wires and a freeform/B-spline fixture report expected body/face/wire/edge counts, adjacency, orientation and surface kinds.
2. **Identity:** selecting a face, regenerating viewport tessellation, saving and reopening preserves the same stable topology reference or reports a controlled migration failure.
3. **Exact geometry:** line and circular edges retain type and parameters within declared model tolerance; spline edges are not silently polygonized. Units and transforms round-trip.
4. **Projection:** section and silhouette fixtures match the legacy reference in curve type, loop topology and geometric deviation within documented tolerances.
5. **Unfold:** plane/cylinder/cone boundaries, holes, seams and connected-face adjacency match reference lengths/areas and distortion bounds. Freeform fallback reports its approximation and finite distortion.
6. **Persistence:** a representative STEP workspace imports, displays, projects, unfolds, saves, reopens offline, and repeats the operations without accessing the original external path or converting through OBJ/STL.
7. **Failure behavior:** malformed/unsupported STEP, worker crash, timeout, cancellation, protocol mismatch and missing runtime yield actionable typed errors and leave the editor responsive.
8. **Delivery:** tests run from signed/notarized macOS output and a clean Windows installation with no system Python, Conda, Node or network access.
9. **Parity:** the chosen backend and legacy pythonOCC reference run the same relevant fixtures. Any accepted difference is reviewed and recorded with numerical evidence; changing counts, topology or accuracy cannot be approved as a snapshot update alone.

Performance budgets for startup, representative import and memory must be recorded with the fixtures before release. A regression beyond an agreed budget requires an explicit waiver, but performance never permits replacing canonical B-rep data with a mesh.

## Rollback and replacement criteria

STEP support stays behind the backend feature flag until the mandatory gates pass. Disable the packaged backend and fall back to the previous safe behavior (STEP unavailable in Avalonia; OBJ/STL and existing OpenGeometry functions remain available) if any release artifact:

- corrupts or cannot round-trip topology/identity;
- exceeds the agreed projection/unfold error tolerance;
- cannot be signed/notarized or run offline on a supported clean machine;
- has unresolved license/redistribution obligations;
- repeatedly crashes or hangs in ordinary fixtures; or
- makes the installer size/startup budget unacceptable without an approved exception.

Replacement of the Python backend is encouraged when a cross-platform OCCT binding, native worker, or OpenGeometry release provides the entire contract, has audited licensing and deterministic packages for all supported RIDs, and passes the same suite with equal or better fidelity and operational behavior. Because the contract is backend-neutral, replacement should affect the adapter/composition root rather than editor state or UI.

## Consequences

- STEP parity can be restored incrementally using proven repository code while the active Avalonia architecture remains .NET-first.
- The application carries a larger platform-specific runtime and must own worker lifecycle, protocol compatibility and packaging security.
- Two geometry implementations coexist temporarily, so capability reporting and contract tests are mandatory.
- Foxtrot remains preview-only. OBJ/STL editor imports normalize through packaged OCCT; their tessellation remains a derived viewport representation.
- AUD-016 may proceed against the packaged worker decision; AUD-017 owns the reusable conformance/parity suite described above.

## Repository evidence

The following paths existed when this decision was recorded:

- `src/Domain/Domain.App/Services/IEditor3DOperationService.cs`
- `src/Pathstitch.App/Services/OpenGeometryEditor3DOperationService.cs`
- `src/Pathstitch.App/Services/OpenGeometryKernelBridge.cs`
- `src/Pathstitch.App/Assets/OpenGeometry/opengeometry-worker.mjs`
- `src/Pathstitch.App/Assets/OpenGeometry/package.json`
- `Pathstitch/Pathstitch/Bridge/PythonBridge.swift`
- `pathstitch_core/worker.py`
- `pathstitch_core/step_ops.py`
- `pathstitch_core/surface_unfold.py`
- `pathstitch_core/net_unfold.py`
- `scripts/package_app.sh`
- `native/step_mesh/Cargo.toml`
- `native/step_mesh/src/lib.rs`
- `LICENSE`
