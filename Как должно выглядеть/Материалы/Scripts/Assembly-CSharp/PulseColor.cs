using DG.Tweening;
using UnityEngine;

public class PulseColor : MonoBehaviour
{
	[SerializeField]
	private Material _mat;

	[SerializeField]
	private Color _fromColor;

	[SerializeField]
	private Color _toColor;

	private bool isActive;

	private void OnEnable()
	{
		if (!isActive)
		{
			Sequence s = DOTween.Sequence();
			s.Append(_mat.DOColor(_fromColor, 0.5f));
			s.Append(_mat.DOColor(_toColor, 0.5f)).SetLoops(-1, LoopType.Yoyo);
			isActive = true;
		}
	}
}
