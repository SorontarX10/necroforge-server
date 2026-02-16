using UnityEngine;

namespace GrassSim.Utils
{
    public static class WorldBoundsUtil
    {
        public static Bounds GetWorldBoundsPreferColliders(GameObject go)
        {
            if (!go)
                return new Bounds(Vector3.zero, Vector3.zero);

            var cols = go.GetComponentsInChildren<Collider>();
            if (cols != null && cols.Length > 0)
            {
                Bounds b = cols[0].bounds;
                for (int i = 1; i < cols.Length; i++)
                    b.Encapsulate(cols[i].bounds);
                return b;
            }

            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs != null && rs.Length > 0)
            {
                Bounds b = rs[0].bounds;
                for (int i = 1; i < rs.Length; i++)
                    b.Encapsulate(rs[i].bounds);
                return b;
            }

            return new Bounds(go.transform.position, Vector3.zero);
        }
    }
}
