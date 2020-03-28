using UnityEngine;
using UnityEngine.Rendering;
public class DitheredPipeline : RenderPipeline
{
	RenderTexture shadowMap;
	int shadowMapSize;
	const string shadowsSoftKeyword = "_SHADOWS_SOFT";
	Vector4[] shadowData = new Vector4[maxVisibleLights];
	Matrix4x4[] worldToShadowMatrices = new Matrix4x4[maxVisibleLights];

	bool enableDynamicBatching;
	bool gpuInstancing;


	const int maxVisibleLights = 16;

	static int visibleLightColorsId =
		Shader.PropertyToID("_VisibleLightColors");
	static int visibleLightDirectionsOrPositionsId =
		Shader.PropertyToID("_VisibleLightDirectionsOrPositions");
	static int visibleLightAttenuationsId =
		Shader.PropertyToID("_VisibleLightAttenuations");
	static int visibleLightSpotDirectionsId =
		Shader.PropertyToID("_VisibleLightSpotDirections");
	static int lightIndicesOffsetAndCountID =
		Shader.PropertyToID("unity_LightIndicesOffsetAndCount");
	static int shadowMapId = 
		Shader.PropertyToID("_ShadowMap");
	static int worldToShadowMatricesId =
		Shader.PropertyToID("_WorldToShadowMatrices");
	static int shadowBiasId = 
		Shader.PropertyToID("_ShadowBias");
	static int shadowDataId = 
		Shader.PropertyToID("_ShadowData");
	static int shadowMapSizeId = 
		Shader.PropertyToID("_ShadowMapSize");



	Vector4[] visibleLightColors = new Vector4[maxVisibleLights];
	Vector4[] visibleLightDirectionsOrPositions = new Vector4[maxVisibleLights];
	Vector4[] visibleLightAttenuations = new Vector4[maxVisibleLights];
	Vector4[] visibleLightSpotDirections = new Vector4[maxVisibleLights];


	public DitheredPipeline(bool dynamicBatching, bool instancing, int shadowMapSize)
	{
		GraphicsSettings.lightsUseLinearIntensity = true;
		enableDynamicBatching = dynamicBatching;
		gpuInstancing = instancing;
		this.shadowMapSize = shadowMapSize;
	}

	protected override void Render(ScriptableRenderContext context, Camera[] cameras)
	{
		foreach(Camera cam in cameras)
			Render(context, cam);
	}


	CullingResults cull;
	CommandBuffer cameraBuffer = new CommandBuffer { name = "Render Camera" };
	CommandBuffer shadowBuffer = new CommandBuffer { name = "Render Shadows" };


	void Render(ScriptableRenderContext context, Camera camera)
	{
		//FRUSTRUM CULL
		ScriptableCullingParameters cullingParams;
		if (!camera.TryGetCullingParameters(out cullingParams))
			return;

		//ADD UI TO SCENE VIEW
		#if UNITY_EDITOR
		if (camera.cameraType == CameraType.SceneView)
					ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
		#endif

		//EXECUTE THE CULL
		cull = context.Cull(ref cullingParams);

		//LIGHTING ARRAY GENERATION
		if (cull.visibleLights.Length > 0)
		{
			ConfigureLights();
			RenderShadows(context);
		}
		else//because I don't configure lights if there are no visible lights, this part clears the lights`
		{
			cameraBuffer.SetGlobalVector(lightIndicesOffsetAndCountID, Vector4.zero);
		}

		context.SetupCameraProperties(camera);

		//CLEARFLAGS
		cameraBuffer.ClearRenderTarget(
			(camera.clearFlags & CameraClearFlags.Depth) != 0,
			(camera.clearFlags & CameraClearFlags.Color) != 0,
			camera.backgroundColor
			);

		cameraBuffer.BeginSample("Render Camera");
		cameraBuffer.SetGlobalVectorArray(
			visibleLightColorsId, visibleLightColors
		);
		cameraBuffer.SetGlobalVectorArray(
			visibleLightDirectionsOrPositionsId, visibleLightDirectionsOrPositions
		);
		cameraBuffer.SetGlobalVectorArray(
			visibleLightAttenuationsId, visibleLightAttenuations
		);
		cameraBuffer.SetGlobalVectorArray(
			visibleLightSpotDirectionsId, visibleLightSpotDirections
		);

		context.ExecuteCommandBuffer(cameraBuffer);
		cameraBuffer.Clear();

		//OPAQUES
		var drawSettings = new DrawingSettings(new ShaderTagId("SRPDefaultUnlit"), new SortingSettings(camera))
		{
			enableDynamicBatching = enableDynamicBatching,
			enableInstancing = gpuInstancing
		};

		if (cull.visibleLights.Length > 0)
			drawSettings.perObjectData = PerObjectData.LightData | PerObjectData.LightIndices;



		var filterSettings = new FilteringSettings(RenderQueueRange.opaque);
		context.DrawRenderers(cull, ref drawSettings, ref filterSettings);
		
		//SKYBOX
		context.DrawSkybox(camera);

		//TRANSPARENTS
		SortingSettings transparentSettings = drawSettings.sortingSettings;
		transparentSettings.criteria = SortingCriteria.CommonTransparent;
		drawSettings.sortingSettings = transparentSettings;

		filterSettings.renderQueueRange = RenderQueueRange.transparent;
		context.DrawRenderers(cull, ref drawSettings, ref filterSettings);

		//DEFAULT PIPELINE
#if UNITY_EDITOR
		DrawDefaultPipeline(context, camera);
#endif

		cameraBuffer.EndSample("Render Camera");
		context.ExecuteCommandBuffer(cameraBuffer);
		cameraBuffer.Clear();

		context.Submit();

		//Cleans shadowmap
		if(shadowMap)
		{
			RenderTexture.ReleaseTemporary(shadowMap);
			shadowMap = null;
		}
	}

	Material errorMaterial;


	void DrawDefaultPipeline(ScriptableRenderContext context, Camera camera) {

		if (errorMaterial == null){
			Shader errorShader = Shader.Find("Hidden/InternalErrorShader");
			errorMaterial = new Material(errorShader) {
				hideFlags = HideFlags.HideAndDontSave
			};
		}

		var drawSettings = new DrawingSettings(new ShaderTagId("ForwardBase"), new SortingSettings(camera));


		drawSettings.SetShaderPassName(1, new ShaderTagId("PrepassBase"));
		drawSettings.SetShaderPassName(2, new ShaderTagId("Always"));
		drawSettings.SetShaderPassName(3, new ShaderTagId("Vertex"));
		drawSettings.SetShaderPassName(4, new ShaderTagId("VertexLMRGBM"));
		drawSettings.SetShaderPassName(5, new ShaderTagId("VertexLM"));

	
		drawSettings.overrideMaterial = errorMaterial;

		var filterSettings = new FilteringSettings(RenderQueueRange.all);

		context.DrawRenderers(cull, ref drawSettings, ref filterSettings );


	}


	void ConfigureLights()
	{
		for (int i=0; i < cull.visibleLights.Length; i++)
		{
			if (i == maxVisibleLights)
				break;

			VisibleLight light = cull.visibleLights[i];
			visibleLightColors[i] = light.finalColor;

			Vector4 attenuation = Vector4.zero;
			attenuation.w = 1f;
			Vector4 shadow = Vector4.zero;
			if (light.lightType == LightType.Directional)
			{
				Vector4 v = light.localToWorldMatrix.GetColumn(2);
				v.x = -v.x;
				v.y = -v.y;
				v.z = -v.z;
				visibleLightDirectionsOrPositions[i] = v;
			}
			else
			{
				visibleLightDirectionsOrPositions[i] = light.localToWorldMatrix.GetColumn(3);//this is the position for point lights
				attenuation.x = 1f / Mathf.Max(light.range * light.range, 0.00001f);
				

				if(light.lightType == LightType.Spot)
				{
					Vector4 v = light.localToWorldMatrix.GetColumn(2);
					v.x = -v.x;
					v.y = -v.y;
					v.z = -v.z;
					visibleLightSpotDirections[i] = v;

					float outerRad = Mathf.Deg2Rad * 0.5f * light.spotAngle;
					float outerCos = Mathf.Cos(outerRad);
					float outerTan = Mathf.Tan(outerRad);
					float innerCos = Mathf.Cos(Mathf.Atan(((46f / 64f) * outerTan)));

					float angleRange = Mathf.Max(innerCos - outerCos, 0.001f);
					attenuation.z = 1f / angleRange;
					attenuation.w = -outerCos * attenuation.z;

					//shadows
					Light shadowLight = light.light;
					Bounds shadowBounds;
					//if the light has shadows enabled and the shadows are contacting something thats visible
					if (shadowLight.shadows != LightShadows.None && cull.GetShadowCasterBounds(i, out shadowBounds))
					{
						shadow.x = shadowLight.shadowStrength;
						shadow.y = shadowLight.shadows == LightShadows.Soft ? 1f : 0f;
					}
				}
			}

			visibleLightAttenuations[i] = attenuation;
			shadowData[i] = shadow;
		}

		//TODO: READ UP ON NATIVE LISTS AND JUNK
		if (cull.visibleLights.Length > maxVisibleLights)
		{
			var lightIndicies = cull.GetLightIndexMap(Unity.Collections.Allocator.Temp);

			for (int i = maxVisibleLights; i < cull.visibleLights.Length; i++)
			{
				lightIndicies[i] = -1;
			}
			cull.SetLightIndexMap(lightIndicies);
		}
	}


	void RenderShadows(ScriptableRenderContext context)
	{
		//because we support 16 lights, this will tile the shadowmap into 16 portions
		float tileSize = shadowMapSize / 4;
		Rect tileViewport = new Rect(0f, 0f, tileSize, tileSize);

		shadowMap = RenderTexture.GetTemporary(shadowMapSize, shadowMapSize, 16, RenderTextureFormat.Depth);
		shadowMap.filterMode = FilterMode.Bilinear;
		shadowMap.wrapMode = TextureWrapMode.Clamp;


		CoreUtils.SetRenderTarget(shadowBuffer, shadowMap, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, ClearFlag.Depth);
		//shadowBuffer.SetRenderTarget(shadowMap, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);

		shadowBuffer.BeginSample("Render Shadows");
		context.ExecuteCommandBuffer(shadowBuffer);
		shadowBuffer.Clear();

		for(int i =0; i < cull.visibleLights.Length; i++)
		{
			if (i == maxVisibleLights)//skip this section if the light doesn't have shadows or we've hit the max number of lights
				break;
			if (shadowData[i].x <= 0f)
				continue;

			Matrix4x4 viewMatrix, projectionMatrix;
			ShadowSplitData splitData;
			//because we are essentially rendering the scene from the spotlight's pov, this sets up the spoof VP matricies
			if(!cull.ComputeSpotShadowMatricesAndCullingPrimitives(i, out viewMatrix, out projectionMatrix, out splitData))
			{//if the function was unable to generate useful matricies, just skip the light
				shadowData[i].x = 0f;
				continue;
			}


			//this is used for tiling 
			float tileOffsetX = i % 4;
			float tileOffsetY = i / 4;
			tileViewport.x = tileOffsetX * tileSize;
			tileViewport.y = tileOffsetY * tileSize;

			shadowBuffer.SetViewport(tileViewport);
			//stops the different tiled shadow maps from cross sampling by adding a border around each one
			shadowBuffer.EnableScissorRect(new Rect(tileViewport.x + 4f, tileViewport.y + 4f, tileSize - 8f, tileSize - 8f));

			shadowBuffer.SetViewProjectionMatrices(viewMatrix, projectionMatrix);
			shadowBuffer.SetGlobalFloat(
				shadowBiasId, cull.visibleLights[i].light.shadowBias
			);

			context.ExecuteCommandBuffer(shadowBuffer);
			shadowBuffer.Clear();


			var shadowSettings = new ShadowDrawingSettings(cull, i);
			context.DrawShadows(ref shadowSettings);

			if (SystemInfo.usesReversedZBuffer)//some gpus flip clipspace z
			{
				projectionMatrix.m20 = -projectionMatrix.m20;
				projectionMatrix.m21 = -projectionMatrix.m21;
				projectionMatrix.m22 = -projectionMatrix.m22;
				projectionMatrix.m23 = -projectionMatrix.m23;
			}

			//clip space goes from -1 to 1, but texture space goes from 0 to 1, this will imbue that transformation into the matrix
			var scaleOffset = Matrix4x4.identity;
			scaleOffset.m00 = scaleOffset.m11 = scaleOffset.m22 = 0.5f;
			scaleOffset.m03 = scaleOffset.m13 = scaleOffset.m23 = 0.5f;
			worldToShadowMatrices[i] = scaleOffset * (projectionMatrix * viewMatrix);

			//this handles making sure that each light samples from the right tile of the tiled shadowmap
			var tileMatrix = Matrix4x4.identity;
			tileMatrix.m00 = tileMatrix.m11 = 0.25f;
			tileMatrix.m03 = tileOffsetX * 0.25f;
			tileMatrix.m13 = tileOffsetY * 0.25f;
			worldToShadowMatrices[i] = tileMatrix * worldToShadowMatrices[i];


		}

		shadowBuffer.DisableScissorRect();
		shadowBuffer.SetGlobalTexture(shadowMapId, shadowMap);
		shadowBuffer.SetGlobalMatrixArray(worldToShadowMatricesId, worldToShadowMatrices);
		shadowBuffer.SetGlobalVectorArray( shadowDataId, shadowData	);

		//Used for soft shadows
		float invShadowMapSize = 1f / shadowMapSize;
		shadowBuffer.SetGlobalVector(shadowMapSizeId, new Vector4(invShadowMapSize, invShadowMapSize, shadowMapSize, shadowMapSize));


		CoreUtils.SetKeyword(shadowBuffer, shadowsSoftKeyword, cull.visibleLights[0].light.shadows == LightShadows.Soft);

		shadowBuffer.EndSample("Render Shadows");
		context.ExecuteCommandBuffer(shadowBuffer);
		shadowBuffer.Clear();
	}

}
