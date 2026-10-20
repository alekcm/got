using System.Collections.Generic;
using UnityEngine;

public static class MeshHelper
{
	private static List<Vector3> vertices;

	private static List<Vector3> normals;

	private static List<Color> colors;

	private static List<Vector2> uv1;

	private static List<Vector2> uv2;

	private static List<Vector2> uv3;

	private static List<int> indices;

	private static Dictionary<uint, int> newVertices;

	private static void InitArrays(Mesh mesh)
	{
		vertices = new List<Vector3>(mesh.vertices);
		normals = new List<Vector3>(mesh.normals);
		colors = new List<Color>(mesh.colors);
		uv1 = new List<Vector2>(mesh.uv);
		uv2 = new List<Vector2>(mesh.uv2);
		uv3 = new List<Vector2>(mesh.uv3);
		indices = new List<int>();
	}

	private static void CleanUp()
	{
		vertices = null;
		normals = null;
		colors = null;
		uv1 = null;
		uv2 = null;
		uv3 = null;
		indices = null;
	}

	private static int GetNewVertex4(int i1, int i2)
	{
		int count = vertices.Count;
		uint key = (uint)((i1 << 16) | i2);
		uint key2 = (uint)((i2 << 16) | i1);
		if (newVertices.ContainsKey(key2))
		{
			return newVertices[key2];
		}
		if (newVertices.ContainsKey(key))
		{
			return newVertices[key];
		}
		newVertices.Add(key, count);
		vertices.Add((vertices[i1] + vertices[i2]) * 0.5f);
		if (normals.Count > 0)
		{
			normals.Add((normals[i1] + normals[i2]).normalized);
		}
		if (colors.Count > 0)
		{
			colors.Add((colors[i1] + colors[i2]) * 0.5f);
		}
		if (uv1.Count > 0)
		{
			uv1.Add((uv1[i1] + uv1[i2]) * 0.5f);
		}
		if (uv2.Count > 0)
		{
			uv2.Add((uv2[i1] + uv2[i2]) * 0.5f);
		}
		if (uv3.Count > 0)
		{
			uv3.Add((uv3[i1] + uv3[i2]) * 0.5f);
		}
		return count;
	}

	public static void Subdivide4(Mesh mesh)
	{
		newVertices = new Dictionary<uint, int>();
		InitArrays(mesh);
		int[] triangles = mesh.triangles;
		for (int i = 0; i < triangles.Length; i += 3)
		{
			int num = triangles[i];
			int num2 = triangles[i + 1];
			int num3 = triangles[i + 2];
			int newVertex = GetNewVertex4(num, num2);
			int newVertex2 = GetNewVertex4(num2, num3);
			int newVertex3 = GetNewVertex4(num3, num);
			indices.Add(num);
			indices.Add(newVertex);
			indices.Add(newVertex3);
			indices.Add(num2);
			indices.Add(newVertex2);
			indices.Add(newVertex);
			indices.Add(num3);
			indices.Add(newVertex3);
			indices.Add(newVertex2);
			indices.Add(newVertex);
			indices.Add(newVertex2);
			indices.Add(newVertex3);
		}
		mesh.vertices = vertices.ToArray();
		if (normals.Count > 0)
		{
			mesh.normals = normals.ToArray();
		}
		if (colors.Count > 0)
		{
			mesh.colors = colors.ToArray();
		}
		if (uv1.Count > 0)
		{
			mesh.uv = uv1.ToArray();
		}
		if (uv2.Count > 0)
		{
			mesh.uv2 = uv2.ToArray();
		}
		if (uv3.Count > 0)
		{
			mesh.uv3 = uv3.ToArray();
		}
		mesh.triangles = indices.ToArray();
		CleanUp();
	}

	private static int GetNewVertex9(int i1, int i2, int i3)
	{
		int count = vertices.Count;
		if (i3 == i1 || i3 == i2)
		{
			uint key = (uint)((i1 << 16) | i2);
			if (newVertices.ContainsKey(key))
			{
				return newVertices[key];
			}
			newVertices.Add(key, count);
		}
		vertices.Add((vertices[i1] + vertices[i2] + vertices[i3]) / 3f);
		if (normals.Count > 0)
		{
			normals.Add((normals[i1] + normals[i2] + normals[i3]).normalized);
		}
		if (colors.Count > 0)
		{
			colors.Add((colors[i1] + colors[i2] + colors[i3]) / 3f);
		}
		if (uv1.Count > 0)
		{
			uv1.Add((uv1[i1] + uv1[i2] + uv1[i3]) / 3f);
		}
		if (uv2.Count > 0)
		{
			uv2.Add((uv2[i1] + uv2[i2] + uv2[i3]) / 3f);
		}
		if (uv3.Count > 0)
		{
			uv3.Add((uv3[i1] + uv3[i2] + uv3[i3]) / 3f);
		}
		return count;
	}

	public static void Subdivide9(Mesh mesh)
	{
		newVertices = new Dictionary<uint, int>();
		InitArrays(mesh);
		int[] triangles = mesh.triangles;
		for (int i = 0; i < triangles.Length; i += 3)
		{
			int num = triangles[i];
			int num2 = triangles[i + 1];
			int num3 = triangles[i + 2];
			int newVertex = GetNewVertex9(num, num2, num);
			int newVertex2 = GetNewVertex9(num2, num, num2);
			int newVertex3 = GetNewVertex9(num2, num3, num2);
			int newVertex4 = GetNewVertex9(num3, num2, num3);
			int newVertex5 = GetNewVertex9(num3, num, num3);
			int newVertex6 = GetNewVertex9(num, num3, num);
			int newVertex7 = GetNewVertex9(num, num2, num3);
			indices.Add(num);
			indices.Add(newVertex);
			indices.Add(newVertex6);
			indices.Add(num2);
			indices.Add(newVertex3);
			indices.Add(newVertex2);
			indices.Add(num3);
			indices.Add(newVertex5);
			indices.Add(newVertex4);
			indices.Add(newVertex7);
			indices.Add(newVertex);
			indices.Add(newVertex2);
			indices.Add(newVertex7);
			indices.Add(newVertex3);
			indices.Add(newVertex4);
			indices.Add(newVertex7);
			indices.Add(newVertex5);
			indices.Add(newVertex6);
			indices.Add(newVertex7);
			indices.Add(newVertex6);
			indices.Add(newVertex);
			indices.Add(newVertex7);
			indices.Add(newVertex2);
			indices.Add(newVertex3);
			indices.Add(newVertex7);
			indices.Add(newVertex4);
			indices.Add(newVertex5);
		}
		mesh.vertices = vertices.ToArray();
		if (normals.Count > 0)
		{
			mesh.normals = normals.ToArray();
		}
		if (colors.Count > 0)
		{
			mesh.colors = colors.ToArray();
		}
		if (uv1.Count > 0)
		{
			mesh.uv = uv1.ToArray();
		}
		if (uv2.Count > 0)
		{
			mesh.uv2 = uv2.ToArray();
		}
		if (uv3.Count > 0)
		{
			mesh.uv3 = uv3.ToArray();
		}
		mesh.triangles = indices.ToArray();
		CleanUp();
	}

	public static void Subdivide(Mesh mesh, int level)
	{
		if (level < 2)
		{
			return;
		}
		while (level > 1)
		{
			while (level % 3 == 0)
			{
				Subdivide9(mesh);
				level /= 3;
			}
			while (level % 2 == 0)
			{
				Subdivide4(mesh);
				level /= 2;
			}
			if (level > 3)
			{
				level++;
			}
		}
	}

	public static Mesh DuplicateMesh(Mesh mesh)
	{
		return Object.Instantiate(mesh);
	}
}
