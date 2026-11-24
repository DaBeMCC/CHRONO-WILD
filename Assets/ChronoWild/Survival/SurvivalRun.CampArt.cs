using System.Collections.Generic;
using UnityEngine;

namespace ChronoWild.Survival
{
    public sealed partial class SurvivalRun
    {
        [SerializeField] Material wovenRoofMaterial;
        readonly List<Mesh> campMeshes = new List<Mesh>();

        void BuildWovenRoof(Transform root)
        {
            const int strips = 12;
            const int rows = 5;
            var vertices = new List<Vector3>();
            var uv = new List<Vector2>();
            var indices = new List<int>();
            for (int row = 0; row <= rows; row++)
            {
                float t = row / (float)rows;
                for (int column = 0; column <= strips; column++)
                {
                    float u = column / (float)strips;
                    float edge = Mathf.Sin(column * 2.9f + row * 1.6f);
                    float x = Mathf.Lerp(-1.16f, 1.16f, u);
                    float z = Mathf.Lerp(-1.02f, .98f, t) + edge * (row == 0 || row == rows ? .09f : .028f);
                    float height = Mathf.Lerp(.16f, 1.72f, t) + edge * .028f + Mathf.Sin(u * Mathf.PI) * .065f;
                    vertices.Add(new Vector3(x, height, z));
                    uv.Add(new Vector2(u * 1.15f, t * 1.18f));
                    if (row == rows || column == strips)
                        continue;
                    int a = row * (strips + 1) + column, b = a + strips + 1;
                    indices.Add(a); indices.Add(b); indices.Add(a + 1);
                    indices.Add(a + 1); indices.Add(b); indices.Add(b + 1);
                }
            }
            CampMesh(root, "Cubierta de fibras entrelazadas", vertices, uv, indices, wovenRoofMaterial);
            Pole(root, new Vector3(-1.08f,.15f,-.91f), new Vector3(1.08f,.15f,-.91f), .073f);
            Pole(root, new Vector3(-1.08f,.86f,.02f), new Vector3(1.08f,.85f,.02f), .064f);
        }

        void BuildCollectorBowl(Transform root)
        {
            const int sides = 16;
            float[] radii = { .24f, .34f, .48f, .49f, .445f, .32f, .23f, .005f };
            float[] heights = { .68f, .72f, .87f, .91f, .91f, .79f, .735f, .735f };
            var vertices = new List<Vector3>();
            var uv = new List<Vector2>();
            var indices = new List<int>();
            for (int ring = 0; ring < radii.Length; ring++)
            {
                for (int i = 0; i <= sides; i++)
                {
                    float t = i / (float)sides;
                    float angle = t * Mathf.PI * 2f;
                    float radius = radii[ring] * (1 + Mathf.Sin(angle * 3) * .035f);
                    vertices.Add(new Vector3(Mathf.Cos(angle) * radius, heights[ring], Mathf.Sin(angle) * radius));
                    uv.Add(new Vector2(t, ring / (float)(radii.Length - 1)));
                    if (ring == radii.Length - 1 || i == sides)
                        continue;
                    int a = ring * (sides + 1) + i, b = a + sides + 1;
                    indices.Add(a); indices.Add(b); indices.Add(a + 1);
                    indices.Add(a + 1); indices.Add(b); indices.Add(b + 1);
                }
            }
            CampMesh(root, "Cuenco tallado", vertices, uv, indices, stoneMaterial);
        }

        void CampMesh(Transform parent, string name, List<Vector3> vertices, List<Vector2> uv,
            List<int> indices, Material material)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            var piece = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            piece.transform.SetParent(parent, false);
            piece.GetComponent<MeshFilter>().sharedMesh = mesh;
            piece.GetComponent<MeshRenderer>().sharedMaterial = material;
            campMeshes.Add(mesh);
        }
    }
}
