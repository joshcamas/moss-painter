using UnityEngine;
using UnityEditor;
using Ardenfall.Utility;

namespace ArdenfallEditor.Utility
{
    [CustomEditor(typeof(MossPainter))]
    public class MossPainterEditor : Editor
    {
        private float brushSize = 2;
        private float brushAngleAllowance = 2;

        private bool brushDown;

        private MossPainter Painter => target as MossPainter;

        private void OnEnable()
        {
            Painter.SetScannedMeshFilters(GameObject.FindObjectsOfType<MeshFilter>());
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            brushSize = EditorGUILayout.FloatField("Size", brushSize);
            brushAngleAllowance = EditorGUILayout.FloatField("Angle", brushAngleAllowance);

            if (GUILayout.Button("Clear"))
            {
                Painter.ResetMesh();
            }

        }
        public void OnSceneGUI()
        {
            if (Application.isPlaying)
                return;

            UpdatePainterEvents();
            SceneView.RepaintAll();
        }

        private void UpdatePainterEvents()
        {
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            Selection.activeGameObject = Painter.gameObject;

            if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                brushDown = true;
                Event.current.Use();
            }

            if (Event.current.type == EventType.MouseUp && Event.current.button == 0)
            {
                if (brushDown)
                    Painter.ApplyPaintedMesh();

                brushDown = false;
                Event.current.Use();
            }

            if (brushDown && Event.current.type == EventType.MouseDrag && Event.current.button == 0)
            {
                 UpdatePainting();
            }

            DrawBrush();

        }

        private void Paint(Vector3 position, Vector3 direction,bool add)
        {
            Painter.PaintAtLocation(position, brushSize, add, direction,brushAngleAllowance);
        }

        //Paints in area depending on editor mouse position
        private void UpdatePainting()
        {
            Event e = Event.current;

            RaycastHit hit;
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            
            if(Physics.Raycast(ray.origin, ray.direction, out hit))
            {
                bool add = !e.shift;
                Paint(hit.point, hit.normal, add);
            }

        }


        private void DrawBrush()
        {
            //Draw brush
            Event e = Event.current;

            RaycastHit hit;
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);

            if (Physics.Raycast(ray.origin, ray.direction, out hit))
            {
                //Outside
                Handles.color = GetBrushColor(false);
                Handles.CircleHandleCap(2, hit.point, Quaternion.LookRotation(hit.normal), brushSize,
                EventType.Repaint);

                //Inside
                Handles.color = GetBrushColor(true);
                Handles.DrawSolidDisc(hit.point, hit.normal, brushSize);
            }

        }

        //Returns current brush color 
        private Color GetBrushColor(bool isInner)
        {
            if (Event.current.shift)
                return new Color(1, 0, 0, isInner ? 0.02f : 0.8f);
            else
                return new Color(0, 1, 0, isInner ? 0.02f : 0.8f);
        }


    }
}
