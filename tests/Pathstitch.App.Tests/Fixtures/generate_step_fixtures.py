"""Generates deterministic STEP fixtures with the pinned packaged pythonOCC runtime."""
from pathlib import Path

from OCC.Core.BRepAlgoAPI import BRepAlgoAPI_Cut
from OCC.Core.BRepBuilderAPI import BRepBuilderAPI_MakeEdge, BRepBuilderAPI_MakeFace, BRepBuilderAPI_MakeWire
from OCC.Core.BRepPrimAPI import BRepPrimAPI_MakeBox, BRepPrimAPI_MakeCone, BRepPrimAPI_MakeCylinder
from OCC.Core.GeomAPI import GeomAPI_PointsToBSpline
from OCC.Core.STEPControl import STEPControl_AsIs, STEPControl_Writer
from OCC.Core.TColgp import TColgp_Array1OfPnt
from OCC.Core.gp import gp_Ax2, gp_Dir, gp_Pnt

ROOT = Path(__file__).resolve().parent


def write(path, shapes):
    writer = STEPControl_Writer()
    for shape in shapes:
        writer.Transfer(shape, STEPControl_AsIs)
    assert writer.Write(str(path)) == 1


box = BRepPrimAPI_MakeBox(100.0, 50.0, 30.0).Shape()
cylinder = BRepPrimAPI_MakeCylinder(gp_Ax2(gp_Pnt(150, 25, 0), gp_Dir(0, 0, 1)), 20, 60).Shape()
cone = BRepPrimAPI_MakeCone(gp_Ax2(gp_Pnt(220, 25, 0), gp_Dir(0, 0, 1)), 25, 10, 50).Shape()
plate = BRepPrimAPI_MakeBox(gp_Pnt(280, 0, 0), 80, 60, 5).Shape()
tool = BRepPrimAPI_MakeCylinder(gp_Ax2(gp_Pnt(320, 30, -1), gp_Dir(0, 0, 1)), 10, 7).Shape()
plate_with_hole = BRepAlgoAPI_Cut(plate, tool).Shape()
write(ROOT / "analytic-multibody-hole.step", [box, cylinder, cone, plate_with_hole])

points = TColgp_Array1OfPnt(1, 5)
for index, xyz in enumerate([(0, 0, 0), (8, 4, 0), (16, -3, 0), (24, 5, 0), (32, 0, 0)], 1):
    points.SetValue(index, gp_Pnt(*xyz))
spline = GeomAPI_PointsToBSpline(points).Curve()
spline_edge = BRepBuilderAPI_MakeEdge(spline).Edge()
down = BRepBuilderAPI_MakeEdge(gp_Pnt(32, 0, 0), gp_Pnt(32, -10, 0)).Edge()
bottom = BRepBuilderAPI_MakeEdge(gp_Pnt(32, -10, 0), gp_Pnt(0, -10, 0)).Edge()
up = BRepBuilderAPI_MakeEdge(gp_Pnt(0, -10, 0), gp_Pnt(0, 0, 0)).Edge()
wire_builder = BRepBuilderAPI_MakeWire()
for edge in (spline_edge, down, bottom, up):
    wire_builder.Add(edge)
freeform_face = BRepBuilderAPI_MakeFace(wire_builder.Wire()).Face()
write(ROOT / "bspline-edge-face.step", [freeform_face])
