#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class SelectableChipGroup
{
        private List<string> options;
        private HashSet<int> selectedIndices;
        private Action<HashSet<int>> onSelectionChanged;

        private static readonly Color SelectedBgColor = new(0.54f, 0.54f, 0.54f);
        private static readonly Color UnselectedBgColor = new Color(0.15f, 0.15f, 0.15f, 0.7f);
        private static readonly Color BorderColor = new Color(0.6f, 0.6f, 0.6f);
        private static readonly Color TextColor = Color.white;

        public SelectableChipGroup(List<string> options, HashSet<int> initialSelection = null, Action<HashSet<int>> onSelectionChanged = null)
        {
            this.options = options;
            this.selectedIndices = initialSelection ?? new HashSet<int>();
            this.onSelectionChanged = onSelectionChanged;
        }

        public void Draw()
        {
            float availableWidth = EditorGUIUtility.currentViewWidth - 40f; // Approximate padding
            float currentX = 0;
            float currentY = 0;
            float rowHeight = 28f;
            float spacing = 6f;

            // Pre-calculate chip widths and positions
            List<Rect> chipRects = new List<Rect>();
            float totalHeight = rowHeight;

            GUIStyle labelStyle = GetLabelStyle();

            for (int i = 0; i < options.Count; i++)
            {
                float chipWidth = CalculateChipWidth(options[i], labelStyle);
                
                if (currentX + chipWidth > availableWidth && currentX > 0)
                {
                    currentX = 0;
                    currentY += rowHeight + spacing;
                    totalHeight += rowHeight + spacing;
                }

                chipRects.Add(new Rect(currentX, currentY, chipWidth, 24f));
                currentX += chipWidth + spacing;
            }

            // Reserve the total space needed
            Rect groupRect = EditorGUILayout.GetControlRect(false, totalHeight);
            
            // Draw chips relative to the groupRect
            for (int i = 0; i < options.Count; i++)
            {
                Rect chipRect = chipRects[i];
                chipRect.x += groupRect.x;
                chipRect.y += groupRect.y;
                DrawChip(i, options[i], chipRect, labelStyle);
            }
        }

        private GUIStyle GetLabelStyle()
        {
            return new GUIStyle(EditorStyles.label)
            {
                fontSize = 12,
                normal = { textColor = TextColor },
                padding = new RectOffset(2, 8, 0, 0)
            };
        }

        private float CalculateChipWidth(string label, GUIStyle labelStyle)
        {
            GUIContent content = new GUIContent(label);
            float iconSize = 14f;
            float padding = 8f;
            return labelStyle.CalcSize(content).x + iconSize + padding * 3;
        }

        private void DrawChip(int index, string label, Rect rect, GUIStyle labelStyle)
        {
            bool isSelected = selectedIndices.Contains(index);
            float height = rect.height;
            float padding = 8f;
            float iconSize = 14f;
            
            // Interaction
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            Event e = Event.current;
            if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition))
            {
                if (isSelected)
                    selectedIndices.Remove(index);
                else
                    selectedIndices.Add(index);
                
                onSelectionChanged?.Invoke(selectedIndices);
                e.Use();
            }

            // Draw Background
            Handles.BeginGUI();
            Color oldHandlesColor = Handles.color;
            
            // Draw border
            Handles.color = BorderColor;
            DrawRoundedRect(rect, height * 0.5f);
            
            // Draw inner background
            Rect innerRect = new Rect(rect.x + 1, rect.y + 1, rect.width - 2, rect.height - 2);
            Handles.color = isSelected ? SelectedBgColor : UnselectedBgColor;
            DrawRoundedRect(innerRect, (height - 2) * 0.5f);

            // Draw Icon (Circle)
            float iconX = rect.x + padding;
            float iconY = rect.y + (height - iconSize) * 0.5f;
            Rect iconRect = new Rect(iconX, iconY, iconSize, iconSize);
            Vector3 center = iconRect.center;
            float radius = iconSize * 0.5f;

            Handles.color = Color.white;
            if (isSelected)
            {
                // Draw filled circle with checkmark
                Handles.DrawSolidDisc(center, Vector3.forward, radius);
                
                // Draw checkmark
                Handles.color = new Color(0.2f, 0.2f, 0.2f); // Dark checkmark
                Vector3 p1 = center + new Vector3(-radius * 0.5f, 0, 0);
                Vector3 p2 = center + new Vector3(-radius * 0.1f, radius * 0.4f, 0);
                Vector3 p3 = center + new Vector3(radius * 0.6f, -radius * 0.4f, 0);
                Handles.DrawLine(p1, p2, 1.5f);
                Handles.DrawLine(p2, p3, 1.5f);
            }
            else
            {
                // Draw wire circle
                Handles.DrawWireDisc(center, Vector3.forward, radius, 1.5f);
            }

            Handles.color = oldHandlesColor;
            Handles.EndGUI();

            // Draw Label
            Rect labelRect = new Rect(iconX + iconSize + padding, rect.y, rect.width - iconSize - padding * 2, height);
            GUI.Label(labelRect, label, labelStyle);
        }

        private void DrawRoundedRect(Rect rect, float radius)
        {
            List<Vector3> verts = new List<Vector3>();
            AddArc(verts, new Vector2(rect.xMin + radius, rect.yMin + radius), radius, 180f, 270f);
            verts.Add(new Vector2(rect.xMax - radius, rect.yMin));
            AddArc(verts, new Vector2(rect.xMax - radius, rect.yMin + radius), radius, 270f, 360f);
            verts.Add(new Vector2(rect.xMax, rect.yMax - radius));
            AddArc(verts, new Vector2(rect.xMax - radius, rect.yMax - radius), radius, 0f, 90f);
            verts.Add(new Vector2(rect.xMin + radius, rect.yMax));
            AddArc(verts, new Vector2(rect.xMin + radius, rect.yMax - radius), radius, 90f, 180f);
            verts.Add(new Vector2(rect.xMin, rect.yMin + radius));
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
