using UnityEngine;
using UnityEngine.Rendering;

[CreateAssetMenu(menuName = "Rendering/Dithered Pipeline")]
public class DitheredPipelineAsset : RenderPipelineAsset 
{
	[SerializeField]
	bool dynamicBatching = true;

	[SerializeField]
	bool gpuInstancing = true;

	[SerializeField]
	bool secondaryLightsAreVertexLights = false;

	[SerializeField]
	float shadowDistance = 100f;

	public enum ShadowMapSize
	{
		_256 = 256,
		_512 = 512,
		_1024 = 1024,
		_2048 = 2048,
		_4096 = 4096
	}
	[SerializeField]
	ShadowMapSize shadowMapSize = ShadowMapSize._1024;

	public enum ShadowCascades
	{
		Zero = 0,
		Two = 2,
		Four = 4
	}

	[SerializeField]
	ShadowCascades shadowCascades = ShadowCascades.Four;

	protected override RenderPipeline CreatePipeline()
	{
		QualitySettings.shadows = ShadowQuality.All;
		return new DitheredPipeline(dynamicBatching, gpuInstancing, secondaryLightsAreVertexLights,(int)shadowMapSize, shadowDistance);
	}

}
