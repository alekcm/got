using System;
using UnityEngine;

public class DancingFrog : MonoBehaviour
{
	private enum Direction
	{
		NotSet = 0,
		Left = 1,
		Up = 2,
		Right = 3,
		Down = 4
	}

	private const float rotationSpeed = 50f;

	private const float distanceFromCenter = 1.8f;

	private Vector3 _previousPosition = Vector3.zero;

	private Direction _currentDirection;

	private int _startFrame;

	private void Awake()
	{
		int childCount = base.transform.parent.childCount;
		int siblingIndex = base.transform.GetSiblingIndex();
		float num = 360f / (float)childCount;
		float f = (float)siblingIndex * num * (MathF.PI / 180f);
		float x = Mathf.Cos(f) * 1.8f;
		float z = Mathf.Sin(f) * 1.8f;
		base.transform.localPosition = new Vector3(x, 0f, z);
		_startFrame = siblingIndex * 2 % 6;
	}

	private void Update()
	{
		RotateAroundParentObject();
		SendMessage("ForceUpdate");
		DetermineDirectionAndAnimate();
	}

	private void RotateAroundParentObject()
	{
		Transform parent = base.transform.parent;
		if (parent != null)
		{
			base.transform.RotateAround(parent.position, Vector3.up, 50f * Time.deltaTime);
			base.transform.localRotation = Quaternion.identity;
		}
	}

	private void DetermineDirectionAndAnimate()
	{
		Vector3 vector = base.transform.position - _previousPosition;
		if (vector.magnitude > 1E-05f)
		{
			float num = Mathf.Atan2(vector.z, vector.x) * 57.29578f;
			num = (num + 360f) % 360f;
			if (num >= 45f && num < 135f)
			{
				SetDirection(Direction.Up);
			}
			else if (num >= 135f && num < 225f)
			{
				SetDirection(Direction.Left);
			}
			else if (num >= 225f && num < 315f)
			{
				SetDirection(Direction.Down);
			}
			else
			{
				SetDirection(Direction.Right);
			}
		}
		_previousPosition = base.transform.position;
	}

	private void SetDirection(Direction direction)
	{
		if (_currentDirection != direction)
		{
			_currentDirection = direction;
			string item = "";
			bool flag = false;
			switch (direction)
			{
			case Direction.Left:
				item = "left";
				break;
			case Direction.Up:
				item = "up";
				break;
			case Direction.Right:
				item = "left";
				flag = true;
				break;
			case Direction.Down:
				item = "down";
				break;
			}
			SendMessage("Play", (item, _startFrame));
			SendMessage("SetFlipped", flag);
		}
	}
}
