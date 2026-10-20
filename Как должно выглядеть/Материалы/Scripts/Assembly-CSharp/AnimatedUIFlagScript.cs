using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[ExecuteInEditMode]
public class AnimatedUIFlagScript : BaseMeshEffect
{
	[SerializeField]
	private float _scaleModifier = 1f;

	private Image _image;

	private RectTransform _localPositionObject;

	private Mesh _mesh;

	private UIVertex _vert;

	private List<UIVertex> _vertexList = new List<UIVertex>();

	private List<int> _triangleList = new List<int>();

	public float Strength
	{
		get
		{
			return _image.color.a;
		}
		set
		{
			_image.color = new Color(_image.color.r, _image.color.g, _image.color.b, value);
		}
	}

	protected override void Start()
	{
		_mesh = new Mesh();
		_image = GetComponent<Image>();
		_localPositionObject = GetComponent<RectTransform>();
		base.Start();
	}

	public override void ModifyMesh(VertexHelper vh)
	{
		if (!(_localPositionObject == null))
		{
			int currentVertCount = vh.currentVertCount;
			float x = _localPositionObject.sizeDelta.x;
			float y = _localPositionObject.sizeDelta.y;
			float num = (0f - _localPositionObject.sizeDelta.x) * _localPositionObject.pivot.x;
			float num2 = (0f - _localPositionObject.sizeDelta.y) * _localPositionObject.pivot.y;
			for (int i = 0; i < currentVertCount; i++)
			{
				vh.PopulateUIVertex(ref _vert, i);
				float x2 = (_vert.position.x - num) / x;
				float y2 = (_vert.position.y - num2) / y;
				_vert.uv1 = new Vector4(x2, y2);
				vh.SetUIVertex(_vert, i);
			}
			IncreaseVertexCount(vh, 16);
		}
	}

	private void IncreaseVertexCount(VertexHelper vh, int res)
	{
		_mesh.Clear();
		vh.FillMesh(_mesh);
		vh.Clear();
		MeshHelper.Subdivide(_mesh, res);
		_vertexList.Clear();
		for (int i = 0; i < _mesh.vertexCount; i++)
		{
			_vertexList.Add(new UIVertex
			{
				position = _mesh.vertices[i],
				tangent = _mesh.tangents[i],
				normal = _mesh.normals[i],
				uv0 = _mesh.uv[i],
				uv1 = _mesh.uv2[i],
				uv2 = _mesh.uv3[i],
				uv3 = _mesh.uv4[i],
				color = _mesh.colors[i]
			});
		}
		_triangleList.Clear();
		for (int j = 0; j < _mesh.triangles.Length; j++)
		{
			_triangleList.Add(_mesh.triangles[j]);
		}
		vh.AddUIVertexStream(_vertexList, _triangleList);
	}
}
