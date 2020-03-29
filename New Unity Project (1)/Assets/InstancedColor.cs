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

		color = Random.ColorHSV() * (2f*Random.value) + Color.white*0.5f;
		color.a = 1f;
		if (propertyBlock == null)
			propertyBlock = new MaterialPropertyBlock();

		propertyBlock.SetColor(colorID, color);
		GetComponent<MeshRenderer>().SetPropertyBlock(propertyBlock);
    }

}
