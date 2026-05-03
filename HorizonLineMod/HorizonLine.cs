using UnityEngine;
using UnityEngine.UI;

namespace HorizonLineMod;

public class HorizonLine : MonoBehaviour
{
    private GameObject longLine;
    private Image longLineImage;
    private Texture2D longLineTexture;
    private Sprite longLineSprite;
    
    private GameObject shortLineContainer;
    private Image shortLineLeftImage;
    private Image shortLineRightImage;
    private Image shortLineUpImage;
    private Texture2D shortLineTexture;
    private Sprite shortLineSprite;
    
    private GameObject noseDot1;
    private Image noseDot1Image;
    private GameObject noseDot2;
    private Image noseDot2Image;
    
    private GameObject flightLine;
    private Image flightLineImage;
    
    private GameObject altitudeTextObj;
    private Text altitudeText;
    private Image altitudeBg;
    
    private Aircraft targetAircraft;

    private float verticalSpeedSmoothed;
    private float flightLateralSmoothed;
    private float flightVerticalSmoothed;
    
    private const float LongLineThickness = 2f;
    private const float ShortLineLength = 12.5f;
    private const float ShortLineThickness = 1.5f;
    private const float ShortLineGap = 10f;
    private const float NoseLineLength = 12.5f;
    private const float NoseLineThickness = 2f;
    private const float NoseLineThreshold = 10f;
    private const float AltitudeTextOffsetY = -25f;
    
    public void Initialize(Aircraft aircraft)
    {
        targetAircraft = aircraft;
        CreateLongLine();
        CreateShortLine();
        CreateNoseDots();
        CreateFlightVector();
        CreateAltitudeText();
    }
    
    private void CreateLongLine()
    {
        float longLineLength = Mathf.Sqrt(Screen.width * Screen.width + Screen.height * Screen.height);
        
        longLine = new GameObject("LongLine");
        longLine.transform.SetParent(transform, false);
        longLine.transform.localPosition = Vector3.zero;
        
        int longTexW = (int)longLineLength;
        int longTexH = (int)LongLineThickness;
        longLineTexture = new Texture2D(longTexW, longTexH, TextureFormat.RGBA32, false);
        Color[] longPixels = new Color[longTexW * longTexH];
for (int i = 0; i < longPixels.Length; i++) longPixels[i] = Color.white;
        longLineTexture.SetPixels(longPixels);
        longLineTexture.Apply();
        
        longLineSprite = Sprite.Create(longLineTexture, new Rect(0, 0, longTexW, longTexH), new Vector2(0.5f, 0.5f));
        
        RectTransform rt = longLine.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(longLineLength, LongLineThickness);
        
        longLineImage = longLine.AddComponent<Image>();
        longLineImage.sprite = longLineSprite;
        longLineImage.color = new Color(0f, 1f, 0f, 0.3f);
    }
    
    private void CreateNoseDots()
    {
        float dotSize = 3f;
        Texture2D dotTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        dotTex.SetPixel(0, 0, Color.white);
        dotTex.Apply();
        Sprite dotSprite = Sprite.Create(dotTex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));

        noseDot1 = new GameObject("NoseDot1");
        noseDot1.transform.SetParent(transform, false);
        noseDot1.transform.localPosition = Vector3.zero;
        RectTransform rt1 = noseDot1.AddComponent<RectTransform>();
        rt1.anchorMin = new Vector2(0.5f, 0.5f);
        rt1.anchorMax = new Vector2(0.5f, 0.5f);
        rt1.pivot = new Vector2(0.5f, 0.5f);
        rt1.sizeDelta = new Vector2(dotSize, dotSize);
        noseDot1Image = noseDot1.AddComponent<Image>();
        noseDot1Image.sprite = dotSprite;
        noseDot1Image.color = new Color(0f, 1f, 0f, 0.7f);
        noseDot1Image.enabled = false;

        noseDot2 = new GameObject("NoseDot2");
        noseDot2.transform.SetParent(transform, false);
        noseDot2.transform.localPosition = Vector3.zero;
        RectTransform rt2 = noseDot2.AddComponent<RectTransform>();
        rt2.anchorMin = new Vector2(0.5f, 0.5f);
        rt2.anchorMax = new Vector2(0.5f, 0.5f);
        rt2.pivot = new Vector2(0.5f, 0.5f);
        rt2.sizeDelta = new Vector2(dotSize, dotSize);
        noseDot2Image = noseDot2.AddComponent<Image>();
        noseDot2Image.sprite = dotSprite;
        noseDot2Image.color = new Color(0f, 1f, 0f, 0.7f);
        noseDot2Image.enabled = false;
    }
    
    private void CreateAltitudeText()
    {
        altitudeTextObj = new GameObject("AltitudeText");
        altitudeTextObj.transform.SetParent(transform, false);
        altitudeTextObj.transform.localPosition = new Vector3(0, AltitudeTextOffsetY, 0);
        
        RectTransform rt = altitudeTextObj.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(64f, 16f);
        
        altitudeText = altitudeTextObj.AddComponent<Text>();
        altitudeText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        altitudeText.fontSize = 14;
        altitudeText.alignment = TextAnchor.MiddleCenter;
        altitudeText.color = new Color(0f, 1f, 0f, 0.7f);
        altitudeText.text = "";

        GameObject bgObj = new GameObject("AltBg");
        bgObj.transform.SetParent(altitudeTextObj.transform, false);
        bgObj.transform.SetAsFirstSibling();
        altitudeBg = bgObj.AddComponent<Image>();
        altitudeBg.color = new Color(1f, 1f, 0f, 0.05f);
        altitudeBg.enabled = false;
        RectTransform bgRt = bgObj.GetComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = new Vector2(-2, -1);
        bgRt.offsetMax = new Vector2(2, 1);
    }
    
    private void CreateShortLine()
    {
        shortLineContainer = new GameObject("ShortLineContainer");
        shortLineContainer.transform.SetParent(transform, false);
        shortLineContainer.transform.localPosition = Vector3.zero;
        
        int shortTexW = (int)ShortLineLength;
        int shortTexH = (int)ShortLineThickness;
        shortLineTexture = new Texture2D(shortTexW, shortTexH, TextureFormat.RGBA32, false);
        Color[] shortPixels = new Color[shortTexW * shortTexH];
for (int i = 0; i < shortPixels.Length; i++) shortPixels[i] = Color.white;
        shortLineTexture.SetPixels(shortPixels);
        shortLineTexture.Apply();
        
        shortLineSprite = Sprite.Create(shortLineTexture, new Rect(0, 0, shortTexW, shortTexH), new Vector2(0.5f, 0.5f));
        
        GameObject shortLineLeft = new GameObject("ShortLineLeft");
        shortLineLeft.transform.SetParent(shortLineContainer.transform, false);
        shortLineLeft.transform.localPosition = new Vector3(-ShortLineGap - ShortLineLength / 2f, 0, 0);
        
        RectTransform rtLeft = shortLineLeft.AddComponent<RectTransform>();
        rtLeft.anchorMin = new Vector2(0.5f, 0.5f);
        rtLeft.anchorMax = new Vector2(0.5f, 0.5f);
        rtLeft.pivot = new Vector2(0.5f, 0.5f);
        rtLeft.sizeDelta = new Vector2(ShortLineLength, ShortLineThickness);
        
        shortLineLeftImage = shortLineLeft.AddComponent<Image>();
        shortLineLeftImage.sprite = shortLineSprite;
        shortLineLeftImage.color = new Color(0f, 1f, 0f, 0.7f);
        
        GameObject shortLineRight = new GameObject("ShortLineRight");
        shortLineRight.transform.SetParent(shortLineContainer.transform, false);
        shortLineRight.transform.localPosition = new Vector3(ShortLineGap + ShortLineLength / 2f, 0, 0);
        
        RectTransform rtRight = shortLineRight.AddComponent<RectTransform>();
        rtRight.anchorMin = new Vector2(0.5f, 0.5f);
        rtRight.anchorMax = new Vector2(0.5f, 0.5f);
        rtRight.pivot = new Vector2(0.5f, 0.5f);
        rtRight.sizeDelta = new Vector2(ShortLineLength, ShortLineThickness);
        
        shortLineRightImage = shortLineRight.AddComponent<Image>();
        shortLineRightImage.sprite = shortLineSprite;
        shortLineRightImage.color = new Color(0f, 1f, 0f, 0.7f);
        
        GameObject shortLineUp = new GameObject("ShortLineUp");
        shortLineUp.transform.SetParent(shortLineContainer.transform, false);
        shortLineUp.transform.localPosition = new Vector3(0, ShortLineGap + ShortLineLength / 2f, 0);
        
        RectTransform rtUp = shortLineUp.AddComponent<RectTransform>();
        rtUp.anchorMin = new Vector2(0.5f, 0.5f);
        rtUp.anchorMax = new Vector2(0.5f, 0.5f);
        rtUp.pivot = new Vector2(0.5f, 0.5f);
        rtUp.sizeDelta = new Vector2(ShortLineThickness, ShortLineLength);
        
        shortLineUpImage = shortLineUp.AddComponent<Image>();
        shortLineUpImage.sprite = shortLineSprite;
        shortLineUpImage.color = new Color(0f, 1f, 0f, 0.7f);
    }
    
    private void LateUpdate()
    {
        if (targetAircraft == null || targetAircraft.disabled || targetAircraft.rb == null)
        {
            SetVisible(false);
            return;
        }
        
        SetVisible(true);
        UpdateLines();
        UpdateNoseDots();
        UpdateFlightVector();
        UpdateAltitudeText();
    }
    
    private void UpdateLines()
    {
        Camera camera = SceneSingleton<CameraStateManager>.i?.mainCamera;
        if (camera == null) return;
        
        float rollAngle = -camera.transform.eulerAngles.z;
        
        float pitch = camera.transform.eulerAngles.x;
        if (pitch > 180f) pitch -= 360f;
        
        float pitchRad = pitch * Mathf.Deg2Rad;
        float halfFovRad = (camera.fieldOfView / 2f) * Mathf.Deg2Rad;
        float pitchOffset = (Mathf.Tan(pitchRad) / Mathf.Tan(halfFovRad)) * 540f;
        
        float rollRad = rollAngle * Mathf.Deg2Rad;
        Vector2 offsetDir = new Vector2(-Mathf.Sin(rollRad), Mathf.Cos(rollRad));
        Vector2 offset = offsetDir * pitchOffset;
        
        if (longLine != null)
        {
            longLine.transform.localEulerAngles = new Vector3(0, 0, rollAngle);
            longLine.transform.localPosition = new Vector3(offset.x, offset.y, 0);
        }
        
        if (shortLineContainer != null)
        {
            shortLineContainer.transform.localEulerAngles = new Vector3(0, 0, rollAngle);
        }
    }
    
    private void UpdateNoseDots()
    {
        if (targetAircraft == null || noseDot1 == null) return;
        
        Camera camera = SceneSingleton<CameraStateManager>.i?.mainCamera;
        if (camera == null) return;
        
        Vector3 aircraftForward = targetAircraft.transform.forward;
        Vector3 cameraForward = camera.transform.forward;
        
        float angle = Vector3.Angle(aircraftForward, cameraForward);
        
        if (angle <= NoseLineThreshold)
        {
            noseDot1Image.enabled = false;
            noseDot2Image.enabled = false;
            return;
        }
        
        Vector3 localDir = camera.transform.InverseTransformDirection(aircraftForward);
        float screenAngle = Mathf.Atan2(localDir.y, localDir.x) * Mathf.Rad2Deg;
        
        float dotAngleSpacing = 15f;
        float radius = 2f * ShortLineLength;
        
        float rad1 = (screenAngle - dotAngleSpacing) * Mathf.Deg2Rad;
        float rad2 = (screenAngle + dotAngleSpacing) * Mathf.Deg2Rad;
        
        noseDot1.transform.localPosition = new Vector3(Mathf.Cos(rad1) * radius, Mathf.Sin(rad1) * radius, 0);
        noseDot2.transform.localPosition = new Vector3(Mathf.Cos(rad2) * radius, Mathf.Sin(rad2) * radius, 0);
        noseDot1Image.enabled = true;
        noseDot2Image.enabled = true;
    }
    
    private void CreateFlightVector()
    {
        int lineW = 13;
        int lineH = 2;
        Texture2D tex = new Texture2D(lineW, lineH, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[lineW * lineH];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
        tex.SetPixels(pixels);
        tex.Apply();
        Sprite spr = Sprite.Create(tex, new Rect(0, 0, lineW, lineH), new Vector2(0f, 0.5f));

        flightLine = new GameObject("FlightVector");
        flightLine.transform.SetParent(transform, false);
        RectTransform rt = flightLine.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.sizeDelta = new Vector2(lineW, lineH);
        flightLineImage = flightLine.AddComponent<Image>();
        flightLineImage.sprite = spr;
        flightLineImage.color = new Color(0f, 1f, 0f, 0.5f);
        flightLineImage.enabled = false;
    }

    private void UpdateFlightVector()
    {
        if (targetAircraft == null || flightLine == null) return;
        var velocity = targetAircraft.rb.velocity;
        float verticalSpeed = velocity.y;
        float lateralSpeed = Vector3.Dot(velocity, targetAircraft.transform.right);
        float smoothing = Mathf.Clamp01(5f * Time.deltaTime);
        flightLateralSmoothed  += (lateralSpeed  - flightLateralSmoothed)  * smoothing;
        flightVerticalSmoothed += (verticalSpeed - flightVerticalSmoothed) * smoothing;
        float magnitude = Mathf.Sqrt(flightLateralSmoothed * flightLateralSmoothed + flightVerticalSmoothed * flightVerticalSmoothed);
        if (magnitude < 0.1f)
        {
            flightLineImage.enabled = false;
            return;
        }

        float flightAngle = Mathf.Atan2(flightVerticalSmoothed, flightLateralSmoothed) * Mathf.Rad2Deg;
        float length = Mathf.Clamp(3f + magnitude * 0.5f, 3f, 13f);
        float radius = 2f * ShortLineLength;

        flightLine.transform.localEulerAngles = new Vector3(0, 0, flightAngle);
        float rad = flightAngle * Mathf.Deg2Rad;
        flightLine.transform.localPosition = new Vector3(Mathf.Cos(rad) * radius, Mathf.Sin(rad) * radius, 0);
        flightLineImage.enabled = true;
        flightLine.GetComponent<RectTransform>().sizeDelta = new Vector2(length, 2f);
    }

    private void UpdateAltitudeText()
    {
        if (targetAircraft == null || altitudeText == null) return;
        
        float radarAlt = targetAircraft.radarAlt;
        float rawVs = Vector3.Dot(targetAircraft.rb.velocity, Vector3.up);
        verticalSpeedSmoothed += (rawVs - verticalSpeedSmoothed) * Mathf.Clamp01(5f * Time.deltaTime);
        float verticalSpeed = verticalSpeedSmoothed;
        
        if (radarAlt < 100f)
        {
            altitudeText.text = $"R {radarAlt:F1} M";
        }
        else
        {
            altitudeText.text = $"{(verticalSpeed >= 0f ? "+" : "-")} {Mathf.Abs(verticalSpeed):F1} M/S";
        }
        altitudeText.color = verticalSpeed >= 0f ? new Color(0f, 1f, 0f, 0.7f) : new Color(1f, 0f, 0f, 0.9f);
        altitudeBg.enabled = verticalSpeed < 0f;
        
        if (shortLineContainer != null)
        {
            float rollAngle = shortLineContainer.transform.localEulerAngles.z;
            float rad = rollAngle * Mathf.Deg2Rad;
            
            float x = -Mathf.Sin(rad) * AltitudeTextOffsetY;
            float y = Mathf.Cos(rad) * AltitudeTextOffsetY;
            
            altitudeTextObj.transform.localPosition = new Vector3(x, y, 0);
            altitudeTextObj.transform.localEulerAngles = new Vector3(0, 0, rollAngle);
        }
    }
    
    private void SetVisible(bool visible)
    {
        if (longLineImage != null) longLineImage.enabled = visible;
        if (shortLineLeftImage != null) shortLineLeftImage.enabled = visible;
        if (shortLineRightImage != null) shortLineRightImage.enabled = visible;
        if (shortLineUpImage != null) shortLineUpImage.enabled = visible;
        if (noseDot1Image != null && !visible) noseDot1Image.enabled = false;
        if (noseDot2Image != null && !visible) noseDot2Image.enabled = false;
        if (flightLineImage != null && !visible) flightLineImage.enabled = false;
        if (altitudeText != null) altitudeText.enabled = visible;
    }
    
    private void OnDestroy()
    {
        if (longLineTexture != null) Destroy(longLineTexture);
        if (longLineSprite != null) Destroy(longLineSprite);
        if (longLine != null) Destroy(longLine);
        
        if (shortLineTexture != null) Destroy(shortLineTexture);
        if (shortLineSprite != null) Destroy(shortLineSprite);
        if (shortLineContainer != null) Destroy(shortLineContainer);
        
        if (noseDot1 != null) Destroy(noseDot1);
        if (noseDot2 != null) Destroy(noseDot2);
        if (flightLine != null) Destroy(flightLine);
        
        if (altitudeTextObj != null) Destroy(altitudeTextObj);
    }
}