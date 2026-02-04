#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class RepartitionGraph
{
    public struct GraphElement
    {
        public float number;
        public string label;
        public Color color;

        public GraphElement(float number, string label, string hexColor)
        {
            this.number = number;
            this.label = label;
            if (!ColorUtility.TryParseHtmlString(hexColor, out this.color))
            {
                this.color = Color.gray;
            }
        }

        public GraphElement(float number, string label, Color color)
        {
            this.number = number;
            this.label = label;
            this.color = color;
        }
    }

    private static readonly float BarHeight = 12f;
    private static readonly float CornerRadius = 3f;
    private static readonly float LegendSquareSize = 12f;
    private static readonly float LegendSpacing = 4f;
    private static readonly float LegendVerticalMargin = 8f;
    private static readonly Color StrokeColor = new Color(0.5f, 0.5f, 0.5f); // #808080

    public void Draw(List<GraphElement> elements)
    {
        if (elements == null || elements.Count == 0) return;

        float total = 0;
        foreach (var element in elements) total += element.number;

        // Reserve space for the bar and the legend
        float totalHeight = BarHeight + LegendVerticalMargin + (elements.Count * (LegendSquareSize + LegendSpacing));
        Rect controlRect = EditorGUILayout.GetControlRect(false, totalHeight);
        
        float currentX = controlRect.x;
        float barY = controlRect.y;

        // Draw the background/stroke for the entire bar
        Rect barRect = new Rect(controlRect.x, barY, controlRect.width, BarHeight);
        Handles.BeginGUI();
        Handles.color = StrokeColor;
        DrawRoundedRect(barRect, CornerRadius);
        Handles.EndGUI();

        // Draw the segments inside the bar, slightly inset to show the stroke
        float segmentHeight = BarHeight - 2f;
        float segmentY = barY + 1f;
        float internalX = controlRect.x + 1f;
        float internalWidth = controlRect.width - 2f;
        float internalCurrentX = internalX;

        for (int i = 0; i < elements.Count; i++)
        {
            float proportion = total > 0 ? elements[i].number / total : 0;
            float width = internalWidth * proportion;
            Rect segmentRect = new Rect(internalCurrentX, segmentY, width, segmentHeight);

            Handles.BeginGUI();
            Color oldColor = Handles.color;
            Handles.color = elements[i].color;

            if (elements.Count == 1)
            {
                DrawRoundedRect(segmentRect, CornerRadius - 1f);
            }
            else if (i == 0)
            {
                DrawLeftRoundedRect(segmentRect, CornerRadius - 1f);
            }
            else if (i == elements.Count - 1)
            {
                DrawRightRoundedRect(segmentRect, CornerRadius - 1f);
            }
            else
            {
                Handles.DrawAAConvexPolygon(new Vector3[] {
                    new Vector2(segmentRect.xMin, segmentRect.yMin),
                    new Vector2(segmentRect.xMax, segmentRect.yMin),
                    new Vector2(segmentRect.xMax, segmentRect.yMax),
                    new Vector2(segmentRect.xMin, segmentRect.yMax)
                });
            }

            Handles.color = oldColor;
            Handles.EndGUI();

            internalCurrentX += width;
        }

        // Draw Legend
        float legendY = barY + BarHeight + LegendVerticalMargin;
        GUIStyle labelStyle = new GUIStyle(EditorStyles.label)
        {
            fontSize = 11,
            normal = { textColor = new Color(0.8f, 0.8f, 0.8f) }
        };

        foreach (var element in elements)
        {
            Rect legendRect = new Rect(controlRect.x, legendY, controlRect.width, LegendSquareSize);
            
            // Stroke for color square (rounded)
            Rect strokeRect = new Rect(legendRect.x, legendRect.y, LegendSquareSize, LegendSquareSize);
            Handles.BeginGUI();
            Handles.color = StrokeColor;
            DrawRoundedRect(strokeRect, CornerRadius);
            
            // Inner color square (rounded)
            Rect innerColorRect = new Rect(strokeRect.x + 1, strokeRect.y + 1, LegendSquareSize - 2, LegendSquareSize - 2);
            Handles.color = element.color;
            DrawRoundedRect(innerColorRect, CornerRadius - 1f);
            Handles.EndGUI();
            
            // Label
            Rect labelRect = new Rect(strokeRect.xMax + LegendSpacing, legendRect.y - 1, legendRect.width - LegendSquareSize - LegendSpacing, LegendSquareSize + 2);
            GUI.Label(labelRect, element.label, labelStyle);

            legendY += LegendSquareSize + LegendSpacing;
        }
    }

    private void DrawRoundedRect(Rect rect, float radius)
    {
        if (radius <= 0)
        {
            Handles.DrawAAConvexPolygon(new Vector3[] {
                new Vector2(rect.xMin, rect.yMin),
                new Vector2(rect.xMax, rect.yMin),
                new Vector2(rect.xMax, rect.yMax),
                new Vector2(rect.xMin, rect.yMax)
            });
            return;
        }
        List<Vector3> verts = new List<Vector3>();
        AddArc(verts, new Vector2(rect.xMin + radius, rect.yMin + radius), radius, 180f, 270f);
        AddArc(verts, new Vector2(rect.xMax - radius, rect.yMin + radius), radius, 270f, 360f);
        AddArc(verts, new Vector2(rect.xMax - radius, rect.yMax - radius), radius, 0f, 90f);
        AddArc(verts, new Vector2(rect.xMin + radius, rect.yMax - radius), radius, 90f, 180f);
        Handles.DrawAAConvexPolygon(verts.ToArray());
    }

    private void DrawLeftRoundedRect(Rect rect, float radius)
    {
        if (radius <= 0)
        {
            Handles.DrawAAConvexPolygon(new Vector3[] {
                new Vector2(rect.xMin, rect.yMin),
                new Vector2(rect.xMax, rect.yMin),
                new Vector2(rect.xMax, rect.yMax),
                new Vector2(rect.xMin, rect.yMax)
            });
            return;
        }
        List<Vector3> verts = new List<Vector3>();
        AddArc(verts, new Vector2(rect.xMin + radius, rect.yMin + radius), radius, 180f, 270f);
        verts.Add(new Vector2(rect.xMax, rect.yMin));
        verts.Add(new Vector2(rect.xMax, rect.yMax));
        AddArc(verts, new Vector2(rect.xMin + radius, rect.yMax - radius), radius, 90f, 180f);
        Handles.DrawAAConvexPolygon(verts.ToArray());
    }

    private void DrawRightRoundedRect(Rect rect, float radius)
    {
        if (radius <= 0)
        {
            Handles.DrawAAConvexPolygon(new Vector3[] {
                new Vector2(rect.xMin, rect.yMin),
                new Vector2(rect.xMax, rect.yMin),
                new Vector2(rect.xMax, rect.yMax),
                new Vector2(rect.xMin, rect.yMax)
            });
            return;
        }
        List<Vector3> verts = new List<Vector3>();
        verts.Add(new Vector2(rect.xMin, rect.yMin));
        AddArc(verts, new Vector2(rect.xMax - radius, rect.yMin + radius), radius, 270f, 360f);
        AddArc(verts, new Vector2(rect.xMax - radius, rect.yMax - radius), radius, 0f, 90f);
        verts.Add(new Vector2(rect.xMin, rect.yMax));
        Handles.DrawAAConvexPolygon(verts.ToArray());
    }

    private void AddArc(List<Vector3> verts, Vector2 center, float radius, float startAngle, float endAngle, int segments = 8)
    {
        float angleStep = (endAngle - startAngle) / segments;
        for (int i = 0; i <= segments; i++)
        {
            float angle = (startAngle + i * angleStep) * Mathf.Deg2Rad;
            verts.Add(new Vector2(center.x + Mathf.Cos(angle) * radius, center.y + Mathf.Sin(angle) * radius));
        }
    }
}
#endif
