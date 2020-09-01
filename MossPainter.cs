using System.Collections.Generic;
using UnityEngine;

using Unity.Collections;
using Unity.Jobs;
using System;

namespace Ardenfall.Utility
{
    public class MossPainter : MonoBehaviour
    {
        [SerializeField] private float mossDistance = 0.01f;

        [SerializeField, HideInInspector]
        private List<Vector3> generatedVertices;

        [SerializeField, HideInInspector]
        private List<int> generatedTriangles;

        [SerializeField, HideInInspector]
        private List<Vector3> generatedNormals;

        //Scanned during editor
        private List<MeshFilter> scannedFilters;
        private List<MeshRenderer> scannedRenderers;

        private List<Vector3[]> scannedVertices;
        private List<Vector3[]> scannedNormals;
        private List<int[]> scannedTriangles;

        private int[] objectBuffer;

        //Found during editor
        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private Mesh mesh;

        //Addition Values
        private List<Vector3> paintingVertices = new List<Vector3>();
        private List<Vector3> paintingNormals = new List<Vector3>();
        private List<int> paintingTriangles = new List<int>();

        //Erasing Values
        private List<Vector3> erasingVertices = new List<Vector3>();
        private List<Vector3> erasingNormals = new List<Vector3>();
        private List<int> erasingTriangles = new List<int>();

        private EditorJobWaiter inflateJobWaiter;

        //Sets all meshes in scene
        public void SetScannedMeshFilters(MeshFilter[] filters)
        {
            scannedFilters = new List<MeshFilter>();
            scannedRenderers = new List<MeshRenderer>();

            foreach (var filter in filters)
            {
                if (filter.sharedMesh == null)
                    continue;

                if (filter.gameObject == gameObject)
                    continue;

                if (!filter.gameObject.isStatic)
                    continue;

                MeshRenderer renderer = filter.GetComponent<MeshRenderer>();

                if (renderer == null || renderer.sharedMaterial == null)
                    continue;

                scannedFilters.Add(filter);
                scannedRenderers.Add(renderer);

            }

            objectBuffer = new int[this.scannedFilters.Count];
            scannedVertices = new List<Vector3[]>();
            scannedTriangles = new List<int[]>();
            scannedNormals = new List<Vector3[]>();

            for (int i = 0; i < scannedFilters.Count; i++)
            {
                scannedVertices.Add(null);
                scannedTriangles.Add(null);
                scannedNormals.Add(null);
            }
        }

        public void ApplyPaintedMesh()
        {
            AddMeshData(paintingVertices, paintingTriangles, paintingNormals, false);
            EraseMeshData(erasingVertices, erasingTriangles, erasingNormals, false);
            GenerateMeshAsync();

            paintingTriangles = new List<int>();
            paintingVertices = new List<Vector3>();
            paintingNormals = new List<Vector3>();

            erasingTriangles = new List<int>();
            erasingVertices = new List<Vector3>();
            erasingNormals = new List<Vector3>();
        }

        public void PaintAtLocation(Vector3 position,float size, bool add, Vector3 paintDirection, float paintDirectionAllowance)
        {
            if (scannedFilters == null)
                return;

            Bounds paintBounds = new Bounds(position, new Vector3(size, size, size));
            float distSqu = Mathf.Pow(size, 2);

            int count = FiltersInBounds(paintBounds, objectBuffer);

            for(int i = 0;i < count; i++)
            {
                //Grab scanned vertices
                if (scannedVertices[objectBuffer[i]] == null)
                {
                    MeshFilter filter = scannedFilters[objectBuffer[i]];

                    if (filter == null || filter.sharedMesh == null)
                        continue;

                    var filterVertices = filter.sharedMesh.vertices;
                    var filterNormals = filter.sharedMesh.normals;

                    //Transform vertices / normals
                    for(int k = 0; k < filterVertices.Length; k++)
                    {
                        filterVertices[k] = filter.transform.TransformPoint(filterVertices[k]);
                        filterNormals[k] = filter.transform.TransformDirection(filterNormals[k]);
                    }

                    scannedVertices[objectBuffer[i]] = filterVertices;
                    scannedNormals[objectBuffer[i]] = filterNormals;

                    scannedTriangles[objectBuffer[i]] = filter.sharedMesh.triangles;
                }

                var vertices = scannedVertices[objectBuffer[i]];
                var triangles = scannedTriangles[objectBuffer[i]];
                var normals = scannedNormals[objectBuffer[i]];

                var targetVertices = add ? paintingVertices : erasingVertices;
                var targetNormals = add ? paintingNormals : erasingNormals;
                var targetTriangles = add ? paintingTriangles : erasingTriangles;

                for (int k = 0; k < triangles.Length; k+=3)
                {
                    //Check angle
                    if (Vector3.Angle(paintDirection, normals[triangles[k]]) > paintDirectionAllowance)
                        continue;

                    //Check each vertex
                    if (Vector3.SqrMagnitude(position - vertices[triangles[k]]) >= distSqu)
                        continue;

                    if (Vector3.SqrMagnitude(position - vertices[triangles[k+1]]) >= distSqu)
                        continue;

                    if (Vector3.SqrMagnitude(position - vertices[triangles[k+2]]) >= distSqu)
                        continue;

                    //Vertices
                    targetVertices.Add(vertices[triangles[k]]);
                    targetVertices.Add(vertices[triangles[k+1]]);
                    targetVertices.Add(vertices[triangles[k+2]]);

                    //Normal
                    targetNormals.Add(normals[triangles[k]]);
                    targetNormals.Add(normals[triangles[k+1]]);
                    targetNormals.Add(normals[triangles[k+2]]);

                    //Triangle
                    int refTriangle = targetTriangles.Count;
                    targetTriangles.Add(refTriangle);
                    targetTriangles.Add(refTriangle + 1);
                    targetTriangles.Add(refTriangle + 2);

                    Color debugColor = add ? Color.green : Color.red;

                    //Debug Triangles
                    Debug.DrawLine(vertices[triangles[k]], vertices[triangles[k + 1]], debugColor, .1f);
                    Debug.DrawLine(vertices[triangles[k+1]], vertices[triangles[k + 2]], debugColor, .1f);
                    Debug.DrawLine(vertices[triangles[k+2]], vertices[triangles[k]], debugColor, .1f);

                }

            }

        }

        public void ResetMesh()
        {
            DestroyImmediate(mesh);

            generatedTriangles = new List<int>();
            generatedVertices = new List<Vector3>();
            generatedNormals = new List<Vector3>();

            paintingTriangles = new List<int>();
            paintingVertices = new List<Vector3>();
            paintingNormals = new List<Vector3>();

            erasingTriangles = new List<int>();
            erasingVertices = new List<Vector3>();
            erasingNormals = new List<Vector3>();

            if (inflateJobWaiter != null && inflateJobWaiter.IsRunning)
                inflateJobWaiter.ForceCancel();
        }

        private void InitializeMesh()
        {
            if (meshFilter == null)
            {
                meshFilter = GetComponent<MeshFilter>();

                if (meshFilter == null)
                    meshFilter = gameObject.AddComponent<MeshFilter>();
            }

            if (meshRenderer == null)
            {
                meshRenderer = GetComponent<MeshRenderer>();

                if (meshRenderer == null)
                    meshRenderer = gameObject.AddComponent<MeshRenderer>();
            }

            if (mesh == null)
            {
                mesh = new Mesh();
                meshFilter.sharedMesh = mesh;
            }

            if (meshFilter.sharedMesh != mesh)
                meshFilter.sharedMesh = mesh;

            if (generatedTriangles == null)
                generatedTriangles = new List<int>();

            if (generatedVertices == null)
                generatedVertices = new List<Vector3>();

            if (generatedNormals == null)
                generatedNormals = new List<Vector3>();

        }

        private void EraseMeshData(List<Vector3> eraseVertices, List<int> eraseTriangles, List<Vector3> eraseNormals, bool generateMesh)
        {
            InitializeMesh();

            bool regenerate = false;

            for (int a = 0; a < eraseTriangles.Count; a += 3)
            {
                bool skipErase = false;

                //Find self duplication (very common and fast to check)
                for (int b = a; b < eraseTriangles.Count; b += 3)
                {
                    if (a == b)
                        continue;

                    if (eraseVertices[eraseTriangles[a]] == eraseVertices[eraseTriangles[b]]
                    && eraseVertices[eraseTriangles[a + 1]] == eraseVertices[eraseTriangles[b + 1]]
                    && eraseVertices[eraseTriangles[a + 2]] == eraseVertices[eraseTriangles[b + 2]])
                    {
                        skipErase = true;
                        break;
                    }
                }

                //Skip!
                if (skipErase)
                    continue;

                //Now try erasing

                int eraseAt = -1;

                for (int b = 0; b < generatedTriangles.Count; b += 3)
                {
                    if (generatedVertices[generatedTriangles[b]] == eraseVertices[eraseTriangles[a]]
                    && generatedVertices[generatedTriangles[b + 1]] == eraseVertices[eraseTriangles[a + 1]]
                    && generatedVertices[generatedTriangles[b + 2]] == eraseVertices[eraseTriangles[a + 2]])
                    {
                        eraseAt = b;
                        break;
                    }
                }

                if (eraseAt == -1)
                    continue;

                int erase0 = generatedTriangles[eraseAt];

                generatedTriangles.RemoveAt(eraseAt);
                generatedTriangles.RemoveAt(eraseAt);
                generatedTriangles.RemoveAt(eraseAt);

                generatedVertices.RemoveAt(erase0);
                generatedVertices.RemoveAt(erase0);
                generatedVertices.RemoveAt(erase0);

                generatedNormals.RemoveAt(erase0);
                generatedNormals.RemoveAt(erase0);
                generatedNormals.RemoveAt(erase0);

                a -= 3;

                //Shift all later triangles down
                for (int i = eraseAt; i < generatedTriangles.Count; i++)
                    generatedTriangles[i] -= 3;

            }

            if (regenerate && generateMesh)
                GenerateMeshAsync();
        }

        private void AddMeshData(List<Vector3> addVertices, List<int> addTriangles, List<Vector3> addNormals, bool generateMesh)
        {
            InitializeMesh();

            bool regenerate = false;

            for (int a = 0; a < addTriangles.Count; a += 3)
            {
                bool skipAddition = false;

                //Find self duplication (very common and fast to check)
                for (int b = a; b < addTriangles.Count; b += 3)
                {
                    if (a == b)
                        continue;

                    if (addVertices[addTriangles[a]] == addVertices[addTriangles[b]]
                    && addVertices[addTriangles[a + 1]] == addVertices[addTriangles[b + 1]]
                    && addVertices[addTriangles[a + 2]] == addVertices[addTriangles[b + 2]])
                    {
                        skipAddition = true;
                        break;
                    }
                }

                //Skip!
                if (skipAddition)
                    continue;
                
                //Find match with existing generated vertices (less common, slow to check)
                for (int b = 0; b < generatedTriangles.Count; b += 3)
                {
                    if (generatedVertices[generatedTriangles[b]] == addVertices[addTriangles[a]]
                    && generatedVertices[generatedTriangles[b + 1]] == addVertices[addTriangles[a + 1]]
                    && generatedVertices[generatedTriangles[b + 2]] == addVertices[addTriangles[a + 2]])
                    {
                        skipAddition = true;
                        break;
                    }
                }

                //Skip!
                if (skipAddition)
                    continue;

                regenerate = true;

                int triangleRef = generatedVertices.Count;

                int index0 = addTriangles[a];
                int index1 = addTriangles[a + 1];
                int index2 = addTriangles[a + 2];

                //Add!
                generatedVertices.Add(addVertices[index0]);
                generatedVertices.Add(addVertices[index1]);
                generatedVertices.Add(addVertices[index2]);

                generatedNormals.Add(addNormals[index0]);
                generatedNormals.Add(addNormals[index1]);
                generatedNormals.Add(addNormals[index2]);

                generatedTriangles.Add(triangleRef);
                generatedTriangles.Add(triangleRef + 1);
                generatedTriangles.Add(triangleRef + 2);

            }

            if (regenerate && generateMesh)
                GenerateMeshAsync();
        }

        private void GenerateMeshAsync()
        {
            if(inflateJobWaiter != null && inflateJobWaiter.IsRunning)
            {
                inflateJobWaiter.ForceCancel();
                return;
            }

            var vertices = new NativeArray<Vector3>(generatedVertices.ToArray(), Allocator.Persistent);
            var normals = new NativeArray<Vector3>(generatedNormals.ToArray(), Allocator.Persistent);
            var modifiedVertices = new NativeArray<Vector3>(generatedVertices.Count, Allocator.Persistent);

            InflateMeshJobMulti inflateJob = new InflateMeshJobMulti()
            {
                vertices = vertices,
                normals = normals,
                modifiedVertices = modifiedVertices,
                inflateAmount = mossDistance
            };

            JobHandle inflatingJobHandle = inflateJob.Schedule(generatedVertices.Count, 64);
            JobHandle.ScheduleBatchedJobs();

            Action<bool> onComplete = (success) =>
            {
                if(success)
                {
                    Vector3[] vertices = new Vector3[generatedVertices.Count];
                    inflateJob.modifiedVertices.CopyTo(vertices);

                    Vector3[] normals = generatedNormals.ToArray();
                    int[] triangles = generatedTriangles.ToArray();

                    GenerateMesh(vertices, triangles, normals);
                }

                inflateJob.vertices.Dispose();
                inflateJob.normals.Dispose();
                inflateJob.modifiedVertices.Dispose();
            };

            inflateJobWaiter = new EditorJobWaiter(inflatingJobHandle, onComplete);
            inflateJobWaiter.Start();
        }

        private void GenerateMesh(Vector3[] vertices, int[] triangles, Vector3[] normals)
        {
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.normals = normals;

            mesh.Optimize();
            mesh.OptimizeIndexBuffers();
            mesh.OptimizeReorderVertexBuffer();
        }

        private int FiltersInBounds(Bounds bounds, int[] buffer)
        {
            int index = 0;

            for (int i = 0; i < scannedRenderers.Count; i++)
            {
                if (scannedRenderers[i] == null)
                    continue;

                if (scannedRenderers[i].bounds.Intersects(bounds))
                {
                    buffer[index] = i;
                    index++;
                }
            }

            return index;
        }

        struct InflateMeshJobMulti : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<Vector3> vertices;

            [ReadOnly]
            public NativeArray<Vector3> normals;
            public float inflateAmount;

            public NativeArray<Vector3> modifiedVertices;

            public void Execute(int i)
            {
                List<int> matchingVertices = new List<int>();
                Vector3 averageDirection = Vector3.zero;

                for (int k = 0; k < vertices.Length; k++)
                {
                    //Allow self match
                    if (vertices[i] == vertices[k])
                    {
                        averageDirection += normals[k];
                        matchingVertices.Add(k);
                    }
                }

                modifiedVertices[i] = vertices[i] + averageDirection * inflateAmount;
            }
        }

    }
}
