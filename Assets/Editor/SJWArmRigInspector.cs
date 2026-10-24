// SJWArmRigInspector_V4.cs
// Read-only. Uses the actual Multi-Aim Constraint reference instead of guessing bone names.

using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class SJWArmRigInspectorV4
{
    [MenuItem("Tools/SJW/Export Arm Twist Runtime Report")]
    public static void ExportReport()
    {
        var sb = new StringBuilder(24000);
        sb.AppendLine("SJW ARM TWIST RUNTIME REPORT V4");
        sb.AppendLine("================================");
        sb.AppendLine($"Unity: {Application.unityVersion}");
        sb.AppendLine($"Scene: {UnityEngine.SceneManagement.SceneManager.GetActiveScene().path}");
        sb.AppendLine($"Playing: {Application.isPlaying}");
        sb.AppendLine();

        var comps = UnityEngine.Object.FindObjectsOfType<Component>(true)
            .Where(c => c != null &&
                        c.GetType().FullName == "UnityEngine.Animations.Rigging.MultiAimConstraint")
            .OrderBy(c => FullPath(c.transform))
            .ToArray();

        if (comps.Length == 0)
        {
            sb.AppendLine("No MultiAimConstraint found.");
        }

        foreach (var comp in comps)
        {
            var so = new SerializedObject(comp);
            var constrainedProp = so.FindProperty("m_Data.m_ConstrainedObject");

            Transform constrained = constrainedProp != null
                ? constrainedProp.objectReferenceValue as Transform
                : null;

            sb.AppendLine($"CONSTRAINT OBJECT: {FullPath(comp.transform)}");
            sb.AppendLine($"Component: {comp.GetType().FullName}");

            if (constrained == null)
            {
                sb.AppendLine("Constrained Object: <NOT FOUND>");
                sb.AppendLine();
                continue;
            }

            sb.AppendLine($"Constrained Object: {FullPath(constrained)}");
            DumpTransform("ROOT TWIST", constrained, sb);

            var children = constrained.GetComponentsInChildren<Transform>(true)
                .Where(t => t != constrained)
                .OrderBy(t => DepthFrom(constrained, t))
                .ThenBy(t => FullPath(t))
                .ToArray();

            sb.AppendLine();
            sb.AppendLine("DESCENDANTS OF CONSTRAINED OBJECT");
            sb.AppendLine("---------------------------------");

            if (children.Length == 0)
            {
                sb.AppendLine("<NO CHILDREN>");
            }
            else
            {
                foreach (var t in children)
                {
                    DumpTransform($"Depth {DepthFrom(constrained, t)} : {t.name}", t, sb);

                    // Verify whether this child has any component capable of writing animation/constraints.
                    var writers = t.GetComponents<Component>()
                        .Where(x => x != null && !(x is Transform))
                        .Select(x => x.GetType().FullName)
                        .ToArray();

                    sb.AppendLine("Non-Transform Components: " +
                        (writers.Length == 0 ? "<none>" : string.Join(", ", writers)));
                }
            }

            sb.AppendLine();
            sb.AppendLine("CONSTRAINT SERIALIZED DATA");
            sb.AppendLine("--------------------------");
            DumpSerialized(comp, sb);
            sb.AppendLine();
            sb.AppendLine("================================");
            sb.AppendLine();
        }

        sb.AppendLine("INTERPRETATION RULE");
        sb.AppendLine("-------------------");
        sb.AppendLine("If Twist1/Twist2 are descendants of ROOT TWIST, their world transforms must inherit the root transform unless another animation/constraint writer overrides them.");
        sb.AppendLine("This report does not change any object.");

        string report = sb.ToString();
        string path = Path.Combine(Application.dataPath, "SJW_ArmTwist_Runtime_Report.txt");
        File.WriteAllText(path, report, new UTF8Encoding(false));
        AssetDatabase.Refresh();
        EditorGUIUtility.systemCopyBuffer = report;

        Debug.Log(report);
        EditorUtility.DisplayDialog(
            "SJW Arm Twist Runtime Report",
            "완료: Assets/SJW_ArmTwist_Runtime_Report.txt\n클립보드에도 복사했습니다.",
            "OK"
        );

        var asset = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/SJW_ArmTwist_Runtime_Report.txt");
        Selection.activeObject = asset;
        EditorGUIUtility.PingObject(asset);
    }

    static void DumpTransform(string label, Transform t, StringBuilder sb)
    {
        sb.AppendLine();
        sb.AppendLine($"[{label}]");
        sb.AppendLine($"Path: {FullPath(t)}");
        sb.AppendLine($"Parent: {(t.parent ? FullPath(t.parent) : "<NONE>")}");
        sb.AppendLine($"Local Position: {Fmt(t.localPosition)}");
        sb.AppendLine($"Local Euler: {Fmt(t.localEulerAngles)}");
        sb.AppendLine($"Local Quaternion: {Fmt(t.localRotation)}");
        sb.AppendLine($"World Position: {Fmt(t.position)}");
        sb.AppendLine($"World Euler: {Fmt(t.eulerAngles)}");
        sb.AppendLine($"World Quaternion: {Fmt(t.rotation)}");
        sb.AppendLine($"World -X: {Fmt(-t.right)}");
    }

    static void DumpSerialized(Component comp, StringBuilder sb)
    {
        try
        {
            var so = new SerializedObject(comp);
            var p = so.GetIterator();
            bool enter = true;
            int n = 0;

            while (p.NextVisible(enter) && n < 220)
            {
                enter = true;
                if (p.depth > 6) continue;
                if (p.propertyPath == "m_Script") continue;

                string v = ValueOf(p);
                if (v == null) continue;

                sb.AppendLine($"{p.propertyPath} = {v}");
                n++;
            }
        }
        catch (Exception e)
        {
            sb.AppendLine($"<Serialized read failed: {e.Message}>");
        }
    }

    static string ValueOf(SerializedProperty p)
    {
        switch (p.propertyType)
        {
            case SerializedPropertyType.Integer: return p.longValue.ToString();
            case SerializedPropertyType.Boolean: return p.boolValue.ToString();
            case SerializedPropertyType.Float: return p.doubleValue.ToString("G17");
            case SerializedPropertyType.String: return p.stringValue;
            case SerializedPropertyType.ObjectReference:
                return p.objectReferenceValue
                    ? $"{FullObjectPath(p.objectReferenceValue)} ({p.objectReferenceValue.GetType().Name})"
                    : "<None>";
            case SerializedPropertyType.Enum:
                return (p.enumValueIndex >= 0 && p.enumValueIndex < p.enumDisplayNames.Length)
                    ? p.enumDisplayNames[p.enumValueIndex]
                    : p.enumValueIndex.ToString();
            case SerializedPropertyType.Vector3: return Fmt(p.vector3Value);
            case SerializedPropertyType.Quaternion: return Fmt(p.quaternionValue);
            default: return null;
        }
    }

    static string FullObjectPath(UnityEngine.Object o)
    {
        if (o is Transform t) return FullPath(t);
        if (o is GameObject go) return FullPath(go.transform);
        if (o is Component c) return FullPath(c.transform);
        return o.name;
    }

    static int DepthFrom(Transform root, Transform t)
    {
        int d = 0;
        var cur = t;
        while (cur != null && cur != root)
        {
            d++;
            cur = cur.parent;
        }
        return cur == root ? d : 999;
    }

    static string FullPath(Transform t)
    {
        if (!t) return "<NULL>";
        var names = new System.Collections.Generic.List<string>();
        while (t != null)
        {
            names.Add(t.name);
            t = t.parent;
        }
        names.Reverse();
        return string.Join("/", names);
    }

    static string Fmt(Vector3 v) =>
        $"({v.x:F9}, {v.y:F9}, {v.z:F9})";

    static string Fmt(Quaternion q) =>
        $"({q.x:F9}, {q.y:F9}, {q.z:F9}, {q.w:F9})";
}
