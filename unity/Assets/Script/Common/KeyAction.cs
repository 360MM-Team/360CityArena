using System;
using UnityEngine;

namespace MovieMap.Core
{
    [Serializable]
    public class KeyAction
    {
        public bool wKey;
        public bool sKey;
        public bool aKey;
        public bool dKey;
        public bool qKey;
        public bool eKey;
        public bool leftArrow;
        public bool rightArrow;
        public bool upArrow;
        public bool enter;

        public KeyAction()
        {
            // Initialize all keys to false.
            wKey = false;
            sKey = false;
            aKey = false;
            dKey = false;
            qKey = false;
            eKey = false;
            leftArrow = false;
            rightArrow = false;
            upArrow = false;
            enter = false;
        }

        public KeyAction(KeyActionDTO dto)
        {
            wKey = dto.wKey;
            sKey = dto.sKey;
            aKey = dto.aKey;
            dKey = dto.dKey;
            qKey = dto.qKey;
            eKey = dto.eKey;
            leftArrow = dto.leftArrow;
            rightArrow = dto.rightArrow;
            upArrow = dto.upArrow;
            enter = dto.enter;
        }

        public KeyActionDTO ToDTO()
        {
            return new KeyActionDTO
            {
                wKey = this.wKey,
                sKey = this.sKey,
                aKey = this.aKey,
                dKey = this.dKey,
                qKey = this.qKey,
                eKey = this.eKey,
                leftArrow = this.leftArrow,
                rightArrow = this.rightArrow,
                upArrow = this.upArrow,
                enter = this.enter
            };
        }

        public override string ToString()
        {
            var keys = new System.Collections.Generic.List<string>();
            if (wKey) keys.Add("W");
            if (sKey) keys.Add("S");
            if (aKey) keys.Add("A");
            if (dKey) keys.Add("D");
            if (qKey) keys.Add("Q");
            if (eKey) keys.Add("E");
            if (leftArrow) keys.Add("Left");
            if (rightArrow) keys.Add("Right");
            if (upArrow) keys.Add("Up");
            if (enter) keys.Add("Enter");
            return string.Join(", ", keys);
        }
    }

    [Serializable]
    public class KeyActionDTO
    {
        public bool wKey;
        public bool sKey;
        public bool aKey;
        public bool dKey;
        public bool qKey;
        public bool eKey;
        public bool leftArrow;
        public bool rightArrow;
        public bool upArrow;
        public bool enter;
    }
}
