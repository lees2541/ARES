using System;
using UnityEngine;

namespace DungeonProto
{
    /// <summary>
    /// 설정 값을 인스펙터에 한글 이름·설명·조절 범위(슬라이더)로 보여 주기 위한 표시용 특성.
    /// 그리는 쪽은 Editor/SettingDrawer.cs.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class SettingAttribute : PropertyAttribute
    {
        public readonly string label;
        public readonly string tip;
        public readonly float min, max;
        public readonly bool hasRange;

        public SettingAttribute(string label, string tip = null)
        {
            this.label = label;
            this.tip = tip;
        }

        public SettingAttribute(string label, float min, float max, string tip = null)
        {
            this.label = label;
            this.tip = tip;
            this.min = min;
            this.max = max;
            hasRange = true;
        }
    }
}
