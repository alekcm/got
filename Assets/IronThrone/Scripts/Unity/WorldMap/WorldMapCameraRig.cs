// IronThrone: камера глобальной карты. Перспектива ~52° сверху, полёт:
// WASD/стрелки — панорама, скролл — зум, средняя кнопка — тянуть карту.
using UnityEngine;

namespace IronThrone.Unity
{
    public class WorldMapCameraRig : MonoBehaviour
    {
        public const float Pitch = 52f;
        public const float Fov = 50f;

        Camera _cam;
        Vector2 _focus;        // точка на карте, куда смотрит камера (x, z)
        float _dist = 26f;     // дистанция до точки фокуса
        Vector2 _mapSize = new Vector2(60, 45);
        Vector3 _lastMouse;
        bool _dragging;

        public Vector2 Focus => _focus;
        public float Distance => _dist;
        public float PitchRad => Pitch * Mathf.PI / 180f;

        public void Init(Camera cam, Vector2 mapSize)
        {
            _cam = cam;
            _mapSize = mapSize;
            _focus = new Vector2(mapSize.x / 2f, mapSize.y / 2f);
            _dist = Mathf.Clamp(mapSize.y * 0.22f, 30f, 120f);
            Apply();
        }

        /// <summary>Навести камеру на точку карты с заданной дистанцией (для витрин/катсцен).</summary>
        public void FocusOn(Vector2 focus, float dist)
        {
            _focus = focus;
            _dist = Mathf.Clamp(dist, 12f, 900f);
            Apply();
        }

        /// <summary>Подобрать дистанцию так, чтобы карта целиком попала в кадр.</summary>
        public void Fit(Vector2 mapSize, float padding = 1.06f)
        {
            _mapSize = mapSize;
            _focus = new Vector2(mapSize.x * 0.5f, mapSize.y * 0.5f);
            // FOV вертикальный; при наклоне 52° земля «сжимается» по вертикали кадра
            float halfAngle = Fov * 0.5f * Mathf.PI / 180f;
            float groundPerUnit = Mathf.Tan(halfAngle) / Mathf.Max(0.2f, Mathf.Sin(PitchRad));
            _dist = Mathf.Clamp(mapSize.y * 0.5f / Mathf.Max(0.05f, groundPerUnit) * padding, 20f, 900f);
            Apply();
        }

        void Update()
        {
            if (_cam == null) return;
            bool changed = false;

            // панорама клавишами
            float panSpeed = Mathf.Max(6f, _dist * 0.7f) * Time.deltaTime;
            float dx = 0f, dz = 0f;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) dz += panSpeed;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) dz -= panSpeed;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) dx += panSpeed;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) dx -= panSpeed;
            if (dx != 0f || dz != 0f)
            {
                _focus.x += dx;
                _focus.y += dz;
                changed = true;
            }

            // тянуть средней кнопкой
            if (Input.GetMouseButtonDown(2))
            {
                _dragging = true;
                _lastMouse = new Vector3(Input.mousePosition.x, Input.mousePosition.y, 0f);
            }
            else if (Input.GetMouseButtonUp(2))
            {
                _dragging = false;
            }
            if (_dragging && Input.GetMouseButton(2))
            {
                var m = new Vector3(Input.mousePosition.x, Input.mousePosition.y, 0f);
                Vector3 delta = m - _lastMouse;
                _lastMouse = m;
                float k = _dist * 0.0016f;
                _focus.x -= delta.x * k;
                _focus.y += delta.y * k; // экран вверх = на север карты
                changed = true;
            }

            // зум скроллом (дистанция вдоль взгляда)
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.001f)
            {
                _dist = Mathf.Clamp(_dist * (1f - scroll * 0.12f), 22f, 900f);
                changed = true;
            }

            // клампим фокус в пределы карты
            float margin = 12f;
            _focus.x = Mathf.Clamp(_focus.x, margin, Mathf.Max(margin + 1f, _mapSize.x - margin));
            _focus.y = Mathf.Clamp(_focus.y, margin, Mathf.Max(margin + 1f, _mapSize.y - margin));

            if (changed) Apply();
        }

        /// <summary>Позиция/поворот камеры по фокусу и дистанции.</summary>
        public void Apply()
        {
            if (_cam == null) return;
            float pr = PitchRad;
            // взгляд: вниз под Pitch градусов, на север (+Z)
            var dir = new Vector3(0f, -Mathf.Sin(pr), Mathf.Cos(pr));
            Vector3 pos = new Vector3(_focus.x, 0f, _focus.y) - dir * _dist;
            _cam.transform.position = pos;
            _cam.transform.rotation = Quaternion.Euler(Pitch, 0f, 0f);
            _cam.fieldOfView = Fov;
        }

        /// <summary>Луч из GUI-точки в точку плоскости земли (y = groundY).</summary>
        public bool GroundPoint(Vector2 guiPos, out Vector3 world, float groundY = 0.1f)
        {
            world = Vector3.zero;
            if (_cam == null) return false;
            float pr = PitchRad;
            float aspect = (float)Screen.width / Screen.height;
            float tanF = Mathf.Tan(Fov * 0.5f * Mathf.PI / 180f);

            // базис камеры (yaw = 0)
            var forward = new Vector3(0f, -Mathf.Sin(pr), Mathf.Cos(pr));
            var right = new Vector3(1f, 0f, 0f);
            var up = new Vector3(0f, Mathf.Cos(pr), Mathf.Sin(pr));

            float nx = guiPos.x / Screen.width * 2f - 1f;
            float ny = (1f - guiPos.y / Screen.height) * 2f - 1f;

            Vector3 dir = forward + right * (nx * tanF * aspect) + up * (ny * tanF);
            Vector3 pos = _cam.transform.position;
            if (dir.y >= -0.0001f) return false;
            float t = (groundY - pos.y) / dir.y;
            if (t < 0f) return false;
            world = pos + dir * t;
            return true;
        }
    }
}
