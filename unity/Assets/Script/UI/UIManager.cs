using UnityEngine;
using UnityEngine.UI;

using MovieMap.Core;

namespace MovieMap.UI
{
    public class UIManager : MonoBehaviour
    {
        GameObject _mapParent, _menu;
        [SerializeField]
        readonly ChangeScene _changeScene;

        [Header("アイコン画像")]
        public Sprite hamburgerSprite;
        public Sprite closeSprite;

        private Image _iconImage;

        void Start()
        {
            _mapParent = GameObject.Find("MapParent");
            MapVisibilitySettings.RegisterMapParent(_mapParent);
            if (!MapVisibilitySettings.IsMapAllowed && _mapParent != null && _mapParent.activeSelf)
            {
                _mapParent.SetActive(false);
            }
            _menu = GameObject.Find("Menu");
            if (Application.isMobilePlatform)
            {
                _menu.SetActive(false);
                _iconImage = GameObject.Find("MenuTrigger").GetComponent<Image>();
                _iconImage.sprite = _menu.activeSelf ? closeSprite : hamburgerSprite;
            }

        }
        void Update()
        {
            if (_mapParent == null)
            {
                return;
            }

            if (!MapVisibilitySettings.IsMapAllowed)
            {
                if (_mapParent.activeSelf)
                {
                    _mapParent.SetActive(false);
                }
                return;
            }

            if (RandomInputManager.GetKeyDown(KeyCode.M) || RandomInputManager.GetButtonDown("Map"))
            {
                _mapParent.SetActive(!_mapParent.activeSelf);
            }
        }
        public void BackToSelectStage()
        {
            _changeScene.GoToTargetScene("AreaSelect");
        }

        public void ToggleMenu()
        {
            if (_menu != null)
            {
                _menu.SetActive(!_menu.activeSelf);
                _iconImage.sprite = _menu.activeSelf ? closeSprite : hamburgerSprite;
            }
        }
    }
}
