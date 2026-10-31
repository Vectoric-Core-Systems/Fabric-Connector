// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// GDScript utility functions the converter maps to the runtime: rotate_toward and type_convert.

namespace Aver.Fabric;

public static partial class GDScript
{
    /// <summary>GDScript's <c>rotate_toward(from, to, delta)</c>: moves the angle <paramref name="from"/> toward <paramref name="to"/> by <paramref name="delta"/> radians
    /// along the shorter way round, without passing <paramref name="to"/>. A negative delta turns away from <paramref name="to"/>, and stops at the opposite angle.</summary>
    public static double RotateToward(double from, double to, double delta)
    {
        double difference = Mathf.AngleDifference(from, to);
        double distance = Math.Abs(difference);
        double step = Math.Clamp(delta, distance - Math.PI, distance);
        return from + (difference >= 0.0 ? step : -step);
    }

    /// <summary>GDScript's <c>type_convert(value, type)</c>: the value as the Variant type (<c>TYPE_INT</c> and so on) by the rules of that type's constructor.
    /// Types this runtime has no conversion for, and values that cannot be converted, give nil.</summary>
    public static Variant TypeConvert(Variant value, long type)
    {
        if (type < 0 || type >= (long)Variant.Type.Max) return default;
        var to = (Variant.Type)type;
        if (to == value.VariantType) return value;
        switch (to)
        {
            case Variant.Type.Nil: return default;
            case Variant.Type.Bool: return Variant.From(value.AsBool());
            case Variant.Type.Int:
                return value.VariantType is Variant.Type.Bool or Variant.Type.Float or Variant.Type.String or Variant.Type.StringName ? Variant.From(value.AsInt64()) : default;
            case Variant.Type.Float:
                return value.VariantType is Variant.Type.Bool or Variant.Type.Int or Variant.Type.String or Variant.Type.StringName ? Variant.From(value.AsDouble()) : default;
            case Variant.Type.String:
                return value.VariantType is Variant.Type.Nil ? default : Variant.From(value.AsString());
            case Variant.Type.StringName:
                return value.VariantType is Variant.Type.String or Variant.Type.NodePath ? Variant.From(value.AsStringName()) : default;
            case Variant.Type.NodePath:
                return value.VariantType is Variant.Type.String or Variant.Type.StringName ? Variant.From(value.AsNodePath()) : default;
            case Variant.Type.Vector2: return value.VariantType == Variant.Type.Vector2I ? Variant.From(value.AsVector2()) : default;
            case Variant.Type.Vector2I: return value.VariantType == Variant.Type.Vector2 ? Variant.From(value.AsVector2I()) : default;
            case Variant.Type.Vector3: return value.VariantType == Variant.Type.Vector3I ? Variant.From(value.AsVector3()) : default;
            case Variant.Type.Vector3I: return value.VariantType == Variant.Type.Vector3 ? Variant.From(value.AsVector3I()) : default;
            default: return default;
        }
    }
}
