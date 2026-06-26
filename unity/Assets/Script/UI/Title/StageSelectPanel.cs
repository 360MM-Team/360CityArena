using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace MovieMap.UI
{
    public class StageSelectPanel : MonoBehaviour
    {
        [SerializeField]
        Text _stageText;
        [SerializeField]
        Text _stageExplainText;

        Toggle _prevToggle;

        void Update()
        {
            var toggle = GetComponent<ToggleGroup>().ActiveToggles().FirstOrDefault();

            if (toggle != null && toggle != _prevToggle) {
                ReWriteText(toggle); 
            }

            _prevToggle = toggle;
        }

        void ReWriteText(Toggle toggle)
        {
            _stageText.text = toggle.transform.Find("StageTitleText").GetComponent<Text>().text;
            _stageExplainText.text = toggle.transform.Find("StageExplainText").GetComponent<Text>().text;
        }
    }
}