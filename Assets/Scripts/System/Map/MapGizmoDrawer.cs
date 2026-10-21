using System.Collections.Generic;
using UnityEngine;

public class MapGizmoDrawer : MonoBehaviour
{
    [Header("デバッグ表示設定")]
    public bool showDoorGizmos = true;

    public struct RoomGizmoInfo
    {
        public Vector3 position;
        public MapChipData chipData;
        public int rotationAngle;
        public bool topDoor;
        public bool bottomDoor;
        public bool leftDoor;
        public bool rightDoor;
    }

    private List<RoomGizmoInfo> drawList = new List<RoomGizmoInfo>();
    private float tileSize = 20f;

    public void Setup(List<RoomGizmoInfo> rooms, float size)
    {
        drawList = rooms;
        tileSize = size;
    }

    public void Clear()
    {
        drawList.Clear();
    }

    private void OnDrawGizmos()
    {
        if (!showDoorGizmos) return;

        foreach (var room in drawList)
        {
            float gizmoHeight = 2f;
            Vector3 center = room.position + Vector3.up * gizmoHeight;

            // チップの枠線
            Gizmos.color = Color.gray;
            Gizmos.DrawWireCube(center, new Vector3(tileSize * 0.9f, 0.1f, tileSize * 0.9f));

            // 扉の位置までの距離（チップの端付近）
            float doorOffset = tileSize * 0.45f;
            float markerSize = 0.25f; // 丸いカラーの大きさ

            // 上 (Z+) の扉
            Gizmos.color = room.topDoor ? Color.cyan : Color.red;
            Gizmos.DrawSphere(center + Vector3.forward * doorOffset, markerSize);

            // 下 (Z-) の扉
            Gizmos.color = room.bottomDoor ? Color.cyan : Color.red;
            Gizmos.DrawSphere(center + Vector3.back * doorOffset, markerSize);

            // 左 (X-) の扉
            Gizmos.color = room.leftDoor ? Color.cyan : Color.red;
            Gizmos.DrawSphere(center + Vector3.left * doorOffset, markerSize);

            // 右 (X+) の扉
            Gizmos.color = room.rightDoor ? Color.cyan : Color.red;
            Gizmos.DrawSphere(center + Vector3.right * doorOffset, markerSize);
        }
    }
}