using System.Collections.Generic;
using UnityEngine;

using Unity.Collections;
using Unity.Jobs;
using System;
using Unity.Burst;
using Unity.Entities.UniversalDelegates;

namespace Ardenfall.Utility
{
    public class MossPainter : MonoBehaviour
    {
        [SerializeField] private float mossDistance = 0.01f;

        [SerializeField, HideInInspector]
        private List<Vector3> generatedVertices = new List<Vector3>();

        [SerializeField, HideInInspector]
        private List<Vector3> generatedNormals = new List<Vector3>();

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

        private bool isPaintAdding;
        private float paintSize;
        private Vector3 paintDirection;
        private float paintDirectionAllowance;
        private List<Vector3> paintPositionFrames;

        private List<Vector3> paintingVertices = new List<Vector3>();
        private List<Vector3> paintingNormals = new List<Vector3>();

        private EditorJobWaiter inflateJobWaiter;
        private EditorJobWaiter findDuplicatesJobWaiter;

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
            BuildPaintList();

            if (isPaintAdding)
                AddMeshData(paintingVertices, paintingNormals,true);
            else
                EraseMeshData(paintingVertices,true);

            paintPositionFrames = new List<Vector3>();

            paintingVertices = new List<Vector3>();
            paintingNormals = new List<Vector3>();
        }

        public void PaintAtLocation(Vector3 position, float size, bool add, Vector3 paintDirection, float paintDirectionAllowance)
        {
            this.isPaintAdding = add;
            this.paintSize = size;
            this.paintDirection = paintDirection;
            this.paintDirectionAllowance = paintDirectionAllowance;

            if (this.paintPositionFrames == null)
                this.paintPositionFrames = new List<Vector3>();

            this.paintPositionFrames.Add(position);
        }

        private void BuildPaintList()
        {
            if (paintPositionFrames.Count == 0)
                return;

            if (scannedFilters == null)
                return;

            Vector3 size = new Vector3(paintSize, paintSize, paintSize);
            float paintSizeSqrd = Mathf.Pow(paintSize, 2);

            Bounds bounds = new Bounds(paintPositionFrames[0], size);

            for(int i = 0; i < paintPositionFrames.Count; i++)
                bounds.Encapsulate(new Bounds(paintPositionFrames[i], size));

            int count = FiltersInBounds(bounds, objectBuffer);

            for(int i = 0; i < count; i++)
            {
                if (!ScanObject(objectBuffer[i]))
                    continue;

                List<int> framesInside = new List<int>();

                for (int k = 0; k < paintPositionFrames.Count; k++)
                {
                    Bounds frameBounds = new Bounds(paintPositionFrames[k], size);
                    if (FilterInBounds(frameBounds, objectBuffer[i]))
                        framesInside.Add(k);
                }

                if (framesInside.Count == 0)
                    continue;

                var vertices = scannedVertices[objectBuffer[i]];
                var triangles = scannedTriangles[objectBuffer[i]];
                var normals = scannedNormals[objectBuffer[i]];

                for (int k = 0; k < triangles.Length; k += 3)
                {
                    //Check angle
                    if (Vector3.Angle(paintDirection, normals[triangles[k]]) > paintDirectionAllowance)
                        continue;

                    //Check if this triangle is in at least one frame's bounds

                    bool vertexInsideA = false;
                    bool vertexInsideB = false;
                    bool vertexInsideC = false;

                    for (int j = 0; j < framesInside.Count; j++)
                    {
                        Vector3 framePos = paintPositionFrames[framesInside[j]];

                        if (!vertexInsideA && Vector3.SqrMagnitude(framePos - vertices[triangles[k]]) < paintSizeSqrd)
                            vertexInsideA = true;

                        if (!vertexInsideB && Vector3.SqrMagnitude(framePos - vertices[triangles[k + 1]]) >= paintSizeSqrd)
                            vertexInsideB = true;

                        if (!vertexInsideC && Vector3.SqrMagnitude(framePos - vertices[triangles[k + 2]]) >= paintSizeSqrd)
                            vertexInsideC = true;

                        if(vertexInsideA && vertexInsideB && vertexInsideC)
                            break;

                    }

                    if (!vertexInsideA || !vertexInsideB || !vertexInsideC)
                        continue;

                    paintingVertices.Add(vertices[triangles[k]]);
                    paintingVertices.Add(vertices[triangles[k + 1]]);
                    paintingVertices.Add(vertices[triangles[k + 2]]);

                    //Normal
                    paintingNormals.Add(normals[triangles[k]]);
                    paintingNormals.Add(normals[triangles[k + 1]]);
                    paintingNormals.Add(normals[triangles[k + 2]]);
                }

            }
        }

        private bool ScanObject(int index)
        {
            if (scannedVertices[index] != null)
                return true;

            MeshFilter filter = scannedFilters[index];

            if (filter == null || filter.sharedMesh == null)
                return false;

            var filterVertices = filter.sharedMesh.vertices;
            var filterNormals = filter.sharedMesh.normals;

            //Transform vertices / normals
            for (int k = 0; k < filterVertices.Length; k++)
            {
                filterVertices[k] = filter.transform.TransformPoint(filterVertices[k]);
                filterNormals[k] = filter.transform.TransformDirection(filterNormals[k]);
            }

            scannedVertices[index] = filterVertices;
            scannedNormals[index] = filterNormals;

            scannedTriangles[index] = filter.sharedMesh.triangles;

            return true;
        }

        public void DisplayPaintGUI(Vector3 position,float size, bool add, Vector3 paintDirection, float paintDirectionAllowance)
        {
            if (scannedFilters == null)
                return;

            Bounds paintBounds = new Bounds(position, new Vector3(size, size, size));
            float distSqu = Mathf.Pow(size, 2);

            int count = FiltersInBounds(paintBounds, objectBuffer);

            for(int i = 0;i < count; i++)
            {
                if (!ScanObject(objectBuffer[i]))
                    continue;

                var vertices = scannedVertices[objectBuffer[i]];
                var triangles = scannedTriangles[objectBuffer[i]];
                var normals = scannedNormals[objectBuffer[i]];

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

            generatedVertices = new List<Vector3>();
            generatedNormals = new List<Vector3>();

            paintingVertices = new List<Vector3>();
            paintingNormals = new List<Vector3>();

            paintPositionFrames = new List<Vector3>();

            if (inflateJobWaiter != null && inflateJobWaiter.IsRunning)
                inflateJobWaiter.ForceCancel();
        }

        private void EraseMeshData(List<Vector3> eraseVertices, bool regenerate)
        {
            FindDuplicateMeshData(eraseVertices, (duplicates) =>
            {
                List<int> removeTriangles = new List<int>();

                for (int a = 0; a < eraseVertices.Count; a += 3)
                {
                    if (duplicates[a] != -1)
                        removeTriangles.Add(duplicates[a]);
                }

                //Remove triangles in reverse, so the old indices still map correctly

                removeTriangles.Sort();

                for(int i = removeTriangles.Count-1; i >= 0; i++)
                {
                    generatedVertices.RemoveAt(removeTriangles[i] - 2);
                    generatedVertices.RemoveAt(removeTriangles[i] - 2);
                    generatedVertices.RemoveAt(removeTriangles[i] - 2);

                    generatedNormals.RemoveAt(removeTriangles[i] - 2);
                    generatedNormals.RemoveAt(removeTriangles[i] - 2);
                    generatedNormals.RemoveAt(removeTriangles[i] - 2);
                }

                if(regenerate)
                    GenerateMeshAsync();
            });
        }

        private void AddMeshData(List<Vector3> addVertices, List<Vector3> addNormals, bool regenerate)
        {
            FindDuplicateMeshData(addVertices, (duplicates) =>
             {
                 for (int i = 0; i < addVertices.Count; i += 3)
                 {
                     if (duplicates[i] != -1)
                         continue;

                     generatedVertices.Add(addVertices[i]);
                     generatedVertices.Add(addVertices[i + 1]);
                     generatedVertices.Add(addVertices[i + 2]);

                     generatedNormals.Add(addNormals[i]);
                     generatedNormals.Add(addNormals[i + 1]);
                     generatedNormals.Add(addNormals[i + 2]);
                 }

                 if (regenerate)
                     GenerateMeshAsync();
             });
        }

        private void FindDuplicateMeshData(List<Vector3> addVertices,Action<int[]> onComplete)
        {
            if (findDuplicatesJobWaiter != null && findDuplicatesJobWaiter.IsRunning)
                findDuplicatesJobWaiter.ForceCancel();

            var newVertices = new NativeArray<Vector3>(addVertices.ToArray(), Allocator.Persistent);
            var oldVertices = new NativeArray<Vector3>(generatedVertices.ToArray(), Allocator.Persistent);
            var duplicateVertices = new NativeArray<int>(addVertices.Count, Allocator.Persistent);

            FindDuplicateMeshJob job = new FindDuplicateMeshJob()
            {
                newVertices = newVertices,
                oldVertices = oldVertices,
                duplicateVertices = duplicateVertices
            };

            JobHandle duplicateJobHandle = job.Schedule(addVertices.Count, 64);
            JobHandle.ScheduleBatchedJobs();

            Action<bool> onJobComplete = (success) =>
            {
                if (success)
                {
                    int[] duplicates = new int[addVertices.Count];
                    job.duplicateVertices.CopyTo(duplicates);
                    onComplete?.Invoke(duplicates);
                }

                job.duplicateVertices.Dispose();
                job.newVertices.Dispose();
                job.oldVertices.Dispose();
            };

            findDuplicatesJobWaiter = new EditorJobWaiter(duplicateJobHandle, onJobComplete);
            findDuplicatesJobWaiter.Start();

        }

        private void GenerateMeshAsync()
        {
            if(inflateJobWaiter != null && inflateJobWaiter.IsRunning)
                inflateJobWaiter.ForceCancel();

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
                    Vector3[] modVertices = new Vector3[generatedVertices.Count];
                    inflateJob.modifiedVertices.CopyTo(modVertices);

                    Vector3[] modNormals = generatedNormals.ToArray();

                    GenerateMesh(modVertices, modNormals);
                }

                inflateJob.vertices.Dispose();
                inflateJob.normals.Dispose();
                inflateJob.modifiedVertices.Dispose();
            };

            inflateJobWaiter = new EditorJobWaiter(inflatingJobHandle, onComplete);
            inflateJobWaiter.Start();
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

        }

        private void GenerateMesh(Vector3[] vertices, Vector3[] normals)
        {
            InitializeMesh();

            int[] triangles = new int[vertices.Length];
            for(int i = 0; i < triangles.Length;i ++)
            {
                triangles[i] = i;
            }

            if (mesh != null)
                DestroyImmediate(mesh);

            mesh = new Mesh();
            meshFilter.sharedMesh = mesh;

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.normals = normals;

            mesh.Optimize();
            mesh.OptimizeIndexBuffers();
            mesh.OptimizeReorderVertexBuffer();
        }

        private bool FilterInBounds(Bounds bounds, int index)
        {
            return scannedRenderers[index].bounds.Intersects(bounds);
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

        [BurstCompile]
        struct FindDuplicateMeshJob: IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<Vector3> newVertices;

            [ReadOnly]
            public NativeArray<Vector3> oldVertices;

            public NativeArray<int> duplicateVertices;

            public void Execute(int i)
            {
                for (int b = 0; b < oldVertices.Length; b += 3)
                {
                    if (oldVertices[b] == newVertices[i]
                    && oldVertices[b + 1] == newVertices[i + 1]
                    && oldVertices[b + 2] == newVertices[i + 2])
                    {
                        duplicateVertices[i] = b;
                        return;
                    }
                }

                duplicateVertices[i] = -1;
            }
        }

        [BurstCompile]
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
                Vector3 averageDirection = Vector3.zero;

                for (int k = 0; k < vertices.Length; k++)
                    if (vertices[i] == vertices[k])
                        averageDirection += normals[k];

                modifiedVertices[i] = vertices[i] + averageDirection * inflateAmount;
            }
        }

    }
}
