using System.Collections.Generic;
using UnityEngine;

// Piercing wounds: punches see-through holes in the fighter's skinned mesh along the line a
// weapon went in and out, so a fighter stabbed again and again ends up riddled.
public class BodyHoles : MonoBehaviour
{
    SkinnedMeshRenderer skin;
    Mesh ownedMesh;
    readonly List<int> keptTriangles = new List<int>();

    public void Punch(Vector3 entry, Vector3 direction, Collider struck, float radius)
    {
        if (direction.sqrMagnitude < 0.000001f)
        {
            direction = transform.forward;
        }
        direction.Normalize();

        // Where it came out: trace back from beyond the far side of the part that was struck.
        Vector3 exit = entry;
        if (struck != null)
        {
            float reach = struck.bounds.extents.magnitude * 2f + 0.1f;
            Ray back = new Ray(entry + direction * reach, -direction);
            if (struck.Raycast(back, out RaycastHit hit, reach))
            {
                exit = hit.point;
            }
        }

        Sliceable sliceable = GetComponent<Sliceable>();
        GameObject blood = sliceable != null ? sliceable._bloodPrefub : null;
        if (blood != null && struck != null)
        {
            Instantiate(blood, entry, Quaternion.LookRotation(-direction), struck.transform);
            if ((exit - entry).sqrMagnitude > 0.0001f)
            {
                Instantiate(blood, exit, Quaternion.LookRotation(direction), struck.transform);
            }
        }

        // The bone colliders sit inside the mesh surface, so carry the line on past both ends
        // to make sure it breaks through the skin front and back.
        float span = FindSkin() ? skin.bounds.extents.magnitude * 0.5f : 0.3f;
        CutTunnel(entry - direction * span, exit + direction * span, radius);
    }

    // Removes every triangle with a corner within radius of the line from -> to (as posed right now).
    void CutTunnel(Vector3 from, Vector3 to, float radius)
    {
        if (!FindSkin())
        {
            return;
        }

        // Work on our own copy so the shared asset is never modified.
        if (skin.sharedMesh != ownedMesh)
        {
            ownedMesh = Instantiate(skin.sharedMesh);
            ownedMesh.name = skin.sharedMesh.name + " (holes)";
            skin.sharedMesh = ownedMesh;
        }

        Mesh mesh = ownedMesh;
        Vector3[] vertices = mesh.vertices;
        BoneWeight[] weights = mesh.boneWeights;
        Matrix4x4[] bindposes = mesh.bindposes;
        Transform[] bones = skin.bones;
        if (weights.Length != vertices.Length || bindposes.Length != bones.Length)
        {
            return;
        }

        Matrix4x4[] skinning = new Matrix4x4[bones.Length];
        for (int i = 0; i < bones.Length; i++)
        {
            skinning[i] = bones[i] != null ? bones[i].localToWorldMatrix * bindposes[i] : skin.transform.localToWorldMatrix;
        }

        float radiusSqr = radius * radius;
        bool[] inside = new bool[vertices.Length];
        int nearest = -1;
        float nearestDistance = float.MaxValue;
        for (int v = 0; v < vertices.Length; v++)
        {
            Vector3 world = Skin(vertices[v], weights[v], skinning);
            float d = DistanceSqrToSegment(world, from, to);
            inside[v] = d <= radiusSqr;
            if (d < nearestDistance)
            {
                nearestDistance = d;
                nearest = v;
            }
        }

        // On a coarse mesh no corner may fall inside a small radius: take the nearest corner's triangles.
        bool anyInside = false;
        for (int v = 0; v < inside.Length && !anyInside; v++)
        {
            anyInside = inside[v];
        }
        if (!anyInside && nearest >= 0)
        {
            inside[nearest] = true;
        }

        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            int[] triangles = mesh.GetTriangles(s);
            keptTriangles.Clear();
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                if (inside[triangles[t]] || inside[triangles[t + 1]] || inside[triangles[t + 2]])
                {
                    continue;
                }

                keptTriangles.Add(triangles[t]);
                keptTriangles.Add(triangles[t + 1]);
                keptTriangles.Add(triangles[t + 2]);
            }

            if (keptTriangles.Count != triangles.Length)
            {
                mesh.SetTriangles(keptTriangles, s, false);
            }
        }
    }

    bool FindSkin()
    {
        if (skin != null)
        {
            return true;
        }

        // The body is the biggest skinned mesh that isn't part of a held weapon.
        int mostVertices = 0;
        foreach (SkinnedMeshRenderer candidate in GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (candidate.sharedMesh == null || candidate.GetComponentInParent<HeldWeapon>() != null)
            {
                continue;
            }

            if (candidate.sharedMesh.vertexCount > mostVertices)
            {
                mostVertices = candidate.sharedMesh.vertexCount;
                skin = candidate;
            }
        }

        return skin != null;
    }

    static Vector3 Skin(Vector3 vertex, BoneWeight w, Matrix4x4[] skinning)
    {
        Vector3 p = Vector3.zero;
        if (w.weight0 > 0f) p += skinning[w.boneIndex0].MultiplyPoint3x4(vertex) * w.weight0;
        if (w.weight1 > 0f) p += skinning[w.boneIndex1].MultiplyPoint3x4(vertex) * w.weight1;
        if (w.weight2 > 0f) p += skinning[w.boneIndex2].MultiplyPoint3x4(vertex) * w.weight2;
        if (w.weight3 > 0f) p += skinning[w.boneIndex3].MultiplyPoint3x4(vertex) * w.weight3;
        return p;
    }

    static float DistanceSqrToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float lengthSqr = ab.sqrMagnitude;
        float t = lengthSqr > 0.000001f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / lengthSqr) : 0f;
        return (a + ab * t - p).sqrMagnitude;
    }
}
