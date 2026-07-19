# Reference-image fidelity fixtures

All fixtures and masks in this directory are generated, copyright-free test data.
Regenerate them with:

```powershell
python -B .\tests\Pathstitch.App.Tests\Fixtures\reference-images\generate_reference_image_fixtures.py
```

Trace gates use source-pixel masks rather than comparing Potrace/Avalonia path nodes:

- clean logo, holes, border, and transparency: IoU at least 0.98; boundary Hausdorff at most 1.5 px;
- antialiased/noisy inputs: IoU at least 0.93; boundary Hausdorff at most 3 px;
- exact component/hole topology for clean fixtures;
- at most 500 output nodes.

Flat and bilinear-gradient background removal require alpha-mask IoU at least 0.98,
with foreground false-positive and false-negative rates at most 1%.

`photo-hair-synthetic-decision-gate.png` is deliberately non-photographic generated data.
It proves deterministic handling only. Pathstitch labels the command "Remove Flat Background"
and does not claim rembg/AI subject-cutout parity. Adding photographic cutout later requires
an approved, pinned offline model, license review, package-size budget, platform runtime,
and separate alpha-IoU/boundary-F1 fixtures.
