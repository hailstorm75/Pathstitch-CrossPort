import { readFileSync } from "node:fs";
import { readFile, writeFile } from "node:fs/promises";
import init, { OGArc, OGPolyline, Vector3, offsetPolylineGroupRegions } from "opengeometry/opengeometry/pkg/opengeometry";

const [, , requestPath, responsePath] = process.argv;

if (!requestPath || !responsePath) {
  console.error("Usage: node opengeometry-worker.mjs <request.json> <response.json>");
  process.exit(2);
}

try {
  const wasm = readFileSync(new URL("./node_modules/opengeometry/opengeometry_bg.wasm", import.meta.url));
  await init({ module_or_path: wasm });

  const request = JSON.parse(await readFile(requestPath, "utf8"));
  const response = execute(request);
  await writeFile(responsePath, JSON.stringify({ ok: true, ...response }), "utf8");
} catch (error) {
  await writeFile(
    responsePath,
    JSON.stringify({
      ok: false,
      error: error instanceof Error ? error.message : String(error),
    }),
    "utf8",
  );
  process.exit(1);
}

function execute(request) {
  switch (request.command) {
    case "offset-curves":
      return offsetCurves(request);
    case "offset-polylines":
      return offsetPolylines(request);
    default:
      throw new Error(`Unsupported OpenGeometry command: ${request.command}`);
  }
}

function offsetCurves(request) {
  const polylines = Array.isArray(request.polylines) ? request.polylines : [];
  const paths = [];

  for (let polylineIndex = 0; polylineIndex < polylines.length; polylineIndex += 1) {
    const polyline = polylines[polylineIndex];
    const points = getCurvePoints(polyline);
    const distance = Number(polyline.distance);
    if (points.length < 2 || !Number.isFinite(distance)) {
      continue;
    }

    const kernelPoints = points.map((point) => new Vector3(Number(point.x), 0, Number(point.y)));
    if (Boolean(polyline.isClosed) && points.length >= 3 && !areSamePoint(points[0], points[points.length - 1])) {
      kernelPoints.push(new Vector3(Number(points[0].x), 0, Number(points[0].y)));
    }

    const kernelPolyline = new OGPolyline(`offset-${polylineIndex}`);
    kernelPolyline.set_config(kernelPoints);
    kernelPolyline.generate_geometry();

    const serialized = kernelPolyline.get_offset_serialized(
      distance,
      Number.isFinite(Number(request.acuteThresholdDegrees)) ? Number(request.acuteThresholdDegrees) : 30,
      Boolean(request.bevel),
    );
    const parsed = JSON.parse(serialized);
    paths.push({
      points: mapKernelPoints(parsed.points),
      isClosed: Boolean(parsed.is_closed),
    });
    kernelPolyline.free();
  }

  return { paths };
}

function getCurvePoints(polyline) {
  const kind = String(polyline.kind ?? "polyline").toLowerCase();
  if (kind === "circle" || kind === "arc") {
    return getArcPoints(polyline, kind === "circle");
  }

  return Array.isArray(polyline.points) ? polyline.points : [];
}

function getArcPoints(polyline, isCircle) {
  const center = polyline.center;
  const radius = Number(polyline.radius);
  if (!center || !Number.isFinite(radius) || radius <= 0) {
    return [];
  }

  const startDegrees = Number.isFinite(Number(polyline.startAngleDegrees)) ? Number(polyline.startAngleDegrees) : 0;
  const endDegrees = Number.isFinite(Number(polyline.endAngleDegrees))
    ? Number(polyline.endAngleDegrees)
    : isCircle ? 360 : startDegrees;
  const segments = Number.isFinite(Number(polyline.segments))
    ? Math.max(1, Math.trunc(Number(polyline.segments)))
    : isCircle ? 48 : 12;

  const arc = new OGArc(`curve-${polyline.kind ?? "arc"}`);
  try {
    arc.set_config(
      new Vector3(Number(center.x), 0, Number(center.y)),
      radius,
      degreesToRadians(startDegrees),
      degreesToRadians(endDegrees),
      segments,
    );
    arc.generate_geometry();
    return mapFlatKernelPoints(JSON.parse(arc.get_geometry_serialized()));
  } finally {
    arc.free();
  }
}

function offsetPolylines(request) {
  const width = Number(request.width);
  if (!Number.isFinite(width) || width <= 0) {
    throw new Error("offset-polylines requires a positive width.");
  }

  const polylines = Array.isArray(request.polylines) ? request.polylines : [];
  const kernelPolylines = [];
  const miterLimit = Number.isFinite(Number(request.miterLimit)) ? Number(request.miterLimit) : 4;

  for (const polyline of polylines) {
    const points = Array.isArray(polyline.points) ? polyline.points : [];
    if (points.length < 2) {
      continue;
    }

    const centreline = [];
    for (let i = 0; i < points.length; i += 1) {
      centreline.push(Number(points[i].x), 0, Number(points[i].y));
    }

    kernelPolylines.push({
      centreline,
      width,
      closed: Boolean(polyline.isClosed),
      miter_limit: miterLimit,
    });
  }

  const result = offsetPolylineGroupRegions(JSON.stringify(kernelPolylines));
  const parsed = JSON.parse(result.regionsSerialized);
  const regions = [];
  for (const region of parsed) {
    regions.push({
      outer: mapKernelPoints(region.outer),
      holes: Array.isArray(region.holes) ? region.holes.map(mapKernelPoints) : [],
    });
  }

  return { regions };
}

function mapKernelPoints(points) {
  if (!Array.isArray(points)) {
    return [];
  }

  return points.map((point) => ({
    x: Number(point.x),
    y: Number(point.z),
  }));
}

function mapFlatKernelPoints(points) {
  if (!Array.isArray(points)) {
    return [];
  }

  const mapped = [];
  for (let index = 0; index + 2 < points.length; index += 3) {
    mapped.push({
      x: Number(points[index]),
      y: Number(points[index + 2]),
    });
  }

  return mapped;
}

function degreesToRadians(value) {
  return (value * Math.PI) / 180;
}

function areSamePoint(left, right) {
  return Math.abs(Number(left.x) - Number(right.x)) <= 1e-9
    && Math.abs(Number(left.y) - Number(right.y)) <= 1e-9;
}
