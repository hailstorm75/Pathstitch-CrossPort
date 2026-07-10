# STEP packaged-worker parity evidence

The executable parity gate is `tests/Pathstitch.App.Tests/Fixtures/verify_step_parity.py`, exercised by
`PinnedWorker_ProtocolMatchesLegacyReferenceWithinDocumentedTolerances`. It sends framed protocol-v1
requests to the packaged worker and independently invokes the legacy pythonOCC entry points with the
same fixtures and operation parameters.

## Tolerances

- DXF curve-length absolute deviation: `1e-6 mm`
- imported face-area absolute deviation: `1e-6 mm²`
- per-sample distortion absolute deviation: `1e-9`
- entity-type counts and closed-loop counts: exact equality

## Windows x64 reference run (2026-07-10)

| Operation / fixture | Protocol result | Legacy result | Maximum deviation |
|---|---:|---:|---:|
| Box/cylinder import topology | 2 bodies; faces 6/3; edges 12/3; surface kinds identical | Same | exact equality |
| Box/cylinder XY projection | 5 polylines, 1 closed loop, 425.4180613717724 mm | Same | 0 mm |
| Planar face unfold | 4 polylines, 160 mm | Same | 0 mm |
| Cylindrical face unfold | 4 polylines, 371.32741228718345 mm | Same | 0 mm |
| Conical face unfold | 4 polylines, 324.2626995923432 mm | Same | 0 mm |
| Holed planar face unfold | 5 polylines, 1 closed loop, 342.65257226562477 mm | Same | 0 mm |
| B-spline-boundary face unfold | 4 polylines, 99.15535040508213 mm | Same | 0 mm |
| Imported face areas | all representative faces | all representative faces | 0 mm² |
| Planar distortion samples | 4 samples | 4 samples | 0 |

The packaged protocol additionally retains canonical projection/unfold curve data: line and circle for
the projection; line for plane/cylinder; line and arc for cone; line and ellipse for the hole; and line
plus the degree-4, five-pole B-spline for the freeform fixture.

## Accepted difference

The legacy compatibility artifact represents sampled non-circular display/export geometry as DXF
`LWPOLYLINE` entities. The packaged response retains the source-derived canonical line, circle, ellipse,
arc, and B-spline parameters and exposes those sampled points separately as `DisplayApproximation`.
The DXF representation remains intentionally identical for compatibility; it is not treated as the
canonical B-rep-derived result.

Native macOS must run the same executable test before release sign-off; cross-built output alone is not
parity evidence.
