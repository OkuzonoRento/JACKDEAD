using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MapGenerator))]
public class MapGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        MapGenerator generator = (MapGenerator)target;

        EditorGUILayout.Space(15);
        EditorGUILayout.LabelField("【 マップ見下ろし図（プレビュー） 】", EditorStyles.boldLabel);

        Rect rect = GUILayoutUtility.GetRect(250, 250);
        EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.15f, 1f));

        var spawnedRooms = generator.GetSpawnedRooms();
        if (spawnedRooms == null || spawnedRooms.Count == 0)
        {
            EditorGUI.DropShadowLabel(rect, "ゲームを再生(Play)すると\nここにマッププレビューが表示されます");
            return;
        }

        Dictionary<Vector2Int, MapGenerator.GeneratedRoom> roomDict = new Dictionary<Vector2Int, MapGenerator.GeneratedRoom>();
        foreach (var r in spawnedRooms)
        {
            if (!roomDict.ContainsKey(r.gridPos))
            {
                roomDict.Add(r.gridPos, r);
            }
        }

        int minX = int.MaxValue, maxX = int.MinValue;
        int minZ = int.MaxValue, maxZ = int.MinValue;

        foreach (var room in spawnedRooms)
        {
            if (room.gridPos.x < minX) minX = room.gridPos.x;
            if (room.gridPos.x > maxX) maxX = room.gridPos.x;
            if (room.gridPos.y < minZ) minZ = room.gridPos.y;
            if (room.gridPos.y > maxZ) maxZ = room.gridPos.y;
        }

        int rangeX = Mathf.Max(1, maxX - minX + 1);
        int rangeZ = Mathf.Max(1, maxZ - minZ + 1);
        int maxRange = Mathf.Max(rangeX, rangeZ);

        float scale = (Mathf.Min(rect.width, rect.height) - 30f) / maxRange;
        Vector2 centerOffset = new Vector2(rect.x + rect.width / 2f, rect.y + rect.height / 2f);
        Vector2 gridCenter = new Vector2((minX + maxX) / 2f, (minZ + maxZ) / 2f);

        // 各部屋と扉を描画
        foreach (var room in spawnedRooms)
        {
            float drawX = centerOffset.x + (room.gridPos.x - gridCenter.x) * scale;
            float drawY = centerOffset.y - (room.gridPos.y - gridCenter.y) * scale;
            Rect roomRect = new Rect(drawX - scale * 0.4f, drawY - scale * 0.4f, scale * 0.8f, scale * 0.8f);

            EditorGUI.DrawRect(roomRect, new Color(0.7f, 0.7f, 0.7f, 0.8f));

            float dotSize = Mathf.Max(3f, scale * 0.15f);
            Color doorColor = new Color(0f, 0.8f, 0.9f, 1f);

            Vector2Int topPos = room.gridPos + Vector2Int.up;
            if (room.topDoor && roomDict.ContainsKey(topPos) && roomDict[topPos].bottomDoor)
            {
                EditorGUI.DrawRect(new Rect(drawX - dotSize / 2f, roomRect.y - dotSize, dotSize, dotSize), doorColor);
            }

            Vector2Int bottomPos = room.gridPos + Vector2Int.down;
            if (room.bottomDoor && roomDict.ContainsKey(bottomPos) && roomDict[bottomPos].topDoor)
            {
                EditorGUI.DrawRect(new Rect(drawX - dotSize / 2f, roomRect.y + roomRect.height, dotSize, dotSize), doorColor);
            }

            Vector2Int leftPos = room.gridPos + Vector2Int.left;
            if (room.leftDoor && roomDict.ContainsKey(leftPos) && roomDict[leftPos].rightDoor)
            {
                EditorGUI.DrawRect(new Rect(roomRect.x - dotSize, drawY - dotSize / 2f, dotSize, dotSize), doorColor);
            }

            Vector2Int rightPos = room.gridPos + Vector2Int.right;
            if (room.rightDoor && roomDict.ContainsKey(rightPos) && roomDict[rightPos].leftDoor)
            {
                EditorGUI.DrawRect(new Rect(roomRect.x + roomRect.width, drawY - dotSize / 2f, dotSize, dotSize), doorColor);
            }
        }

        // --- 【修正】実際にゲーム内で選ばれて生成されたバスの場所だけに黄色い目印を描画 ---
        Vector2Int? busGrid = generator.GetBusGridPos();
        int busDir = generator.GetBusDirection();

        if (busGrid.HasValue && roomDict.ContainsKey(busGrid.Value))
        {
            var busRoom = roomDict[busGrid.Value];
            float drawX = centerOffset.x + (busRoom.gridPos.x - gridCenter.x) * scale;
            float drawY = centerOffset.y - (busRoom.gridPos.y - gridCenter.y) * scale;

            float busOffsetX = 0;
            float busOffsetY = 0;
            float offsetDist = scale * 0.45f;

            // バスが置かれた方向（0:Top, 1:Bottom, 2:Left, 3:Right）に合わせてオフセットを計算
            if (busDir == 0) busOffsetY = -offsetDist;      // Top
            else if (busDir == 1) busOffsetY = offsetDist;   // Bottom
            else if (busDir == 2) busOffsetX = -offsetDist;  // Left
            else if (busDir == 3) busOffsetX = offsetDist;   // Right

            Rect busRect = new Rect(drawX + busOffsetX - scale * 0.2f, drawY + busOffsetY - scale * 0.2f, scale * 0.4f, scale * 0.4f);
            EditorGUI.DrawRect(busRect, new Color(1f, 0.8f, 0f, 1f)); // 黄色（実際のバスの位置）
        }

        if (Application.isPlaying)
        {
            Repaint();
        }
    }
}