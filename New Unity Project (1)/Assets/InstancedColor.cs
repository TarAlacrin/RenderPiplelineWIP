using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class InstancedColor : MonoBehaviour
{
	[SerializeField]
	Color color = Color.white;

	static MaterialPropertyBlock propertyBlock;
	static int colorID = Shader.PropertyToID("_Color");

	private void Awake()
	{
		OnValidate();
	}
	// Start is called before the first frame update
	void OnValidate()
    {

		color = Random.ColorHSV() * (1f + 3f * Random.value) + Color.grey*0.75f;

		if (propertyBlock == null)
			propertyBlock = new MaterialPropertyBlock();

		propertyBlock.SetColor(colorID, color);
		GetComponent<MeshRenderer>().SetPropertyBlock(propertyBlock);
    }

}
