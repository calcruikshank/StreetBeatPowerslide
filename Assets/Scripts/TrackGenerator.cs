using System.Collections.Generic;
using UnityEngine;

public class TrackGenerator : MonoBehaviour
{
    [Header("Track Settings")]
    public GameObject trackSegmentPrefab;       // Prefab of the track segment
    public int initialSegments = 5;            // Number of segments to generate at start
    public float segmentLength = 10f;          // Length of each segment
    public float spawnDistance = 20f;           // Distance ahead of the player to spawn new segments
    public float deleteDistance = 30f;          // Distance behind the player to delete old segments

    [Header("Mesh Generation Settings")]
    public int verticesPerSegment = 10;         // Number of vertices along the width
    public float trackWidth = 5f;               // Width of the track
    public float curveAmplitude = 2f;           // Amplitude of the sine wave for curves
    public float curveFrequency = 0.5f;         // Frequency of the sine wave for curves

    private Queue<GameObject> activeSegments = new Queue<GameObject>();
    private Transform playerTransform;

    private void Start()
    {
        // Find the player (assuming the player has the tag "Player")
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            playerTransform = player.transform;
        }
        else
        {
            Debug.LogError("Player not found! Please tag your player GameObject as 'Player'.");
            return;
        }

        // Generate initial segments
        for (int i = 0; i < initialSegments; i++)
        {
            Vector3 position = transform.position + Vector3.forward * segmentLength * i;
            GameObject segment = Instantiate(trackSegmentPrefab, position, Quaternion.identity, transform);
            GenerateTrackMesh(segment, i);
            activeSegments.Enqueue(segment);
        }
    }

    private void Update()
    {
        if (playerTransform == null) return;

        // Check if we need to add a new segment
        GameObject lastSegment = activeSegments.Peek();
        float lastSegmentZ = lastSegment.transform.position.z;
        if (playerTransform.position.z + spawnDistance > lastSegmentZ)
        {
            AddSegment();
        }

        // Check if we need to remove the first segment
        GameObject firstSegment = activeSegments.Peek();
        if (playerTransform.position.z - deleteDistance > firstSegment.transform.position.z)
        {
            RemoveSegment();
        }
    }

    private void AddSegment()
    {
        int segmentIndex = activeSegments.Count;
        Vector3 position = activeSegments.Peek().transform.position + Vector3.forward * segmentLength;
        GameObject segment = Instantiate(trackSegmentPrefab, position, Quaternion.identity, transform);
        GenerateTrackMesh(segment, segmentIndex);
        activeSegments.Enqueue(segment);
        Debug.Log("Added segment at position: " + position);
    }

    private void RemoveSegment()
    {
        GameObject segment = activeSegments.Dequeue();
        Destroy(segment);
        Debug.Log("Removed segment");
    }

    /// <summary>
    /// Generates a procedurally generated mesh for the given track segment.
    /// </summary>
    /// <param name="segment">The track segment GameObject.</param>
    /// <param name="index">The index of the segment in the queue.</param>
    private void GenerateTrackMesh(GameObject segment, int index)
    {
        MeshFilter meshFilter = segment.GetComponent<MeshFilter>();
        if (meshFilter == null)
        {
            Debug.LogError("TrackSegment prefab missing MeshFilter!");
            return;
        }

        Mesh mesh = new Mesh();
        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        List<Vector2> uvs = new List<Vector2>();

        // Parameters for sine wave
        float zStart = index * segmentLength;
        float zEnd = zStart + segmentLength;
        float deltaZ = segmentLength / (verticesPerSegment - 1);

        for (int i = 0; i < verticesPerSegment; i++)
        {
            float z = zStart + i * deltaZ;
            float sineOffset = Mathf.Sin(z * curveFrequency) * curveAmplitude;

            // Left and right vertices
            Vector3 left = new Vector3(-trackWidth / 2, 0, z - zStart) + new Vector3(sineOffset, 0, 0);
            Vector3 right = new Vector3(trackWidth / 2, 0, z - zStart) + new Vector3(sineOffset, 0, 0);

            vertices.Add(left);
            vertices.Add(right);

            // UVs
            uvs.Add(new Vector2(0, z / segmentLength));
            uvs.Add(new Vector2(1, z / segmentLength));
        }

        // Create triangles
        for (int i = 0; i < verticesPerSegment - 1; i++)
        {
            int start = i * 2;

            // Triangle 1
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 1);

            // Triangle 2
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
        }

        mesh.vertices = vertices.ToArray();
        mesh.triangles = triangles.ToArray();
        mesh.uv = uvs.ToArray();
        mesh.RecalculateNormals();

        meshFilter.mesh = mesh;
    }
}
