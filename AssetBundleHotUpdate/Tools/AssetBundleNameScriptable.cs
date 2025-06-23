using System.Collections.Generic;
using UnityEngine;

namespace AssetBundleHotUpdate
{
    [CreateAssetMenu(fileName = "AB资源名称顺序", menuName = "AssetBundleHotUpdate/创建 AB资源名称顺序")]
    public class AssetBundleNameScriptable : ScriptableObject
    {
        [Header("AssetBundle资源名称列表")] public List<string> assetBundleNames = new List<string>();

        /// <summary>
        /// 添加AssetBundle名称
        /// </summary>
        public void AddAssetBundle(string bundleName)
        {
            if (!assetBundleNames.Contains(bundleName))
            {
                assetBundleNames.Add(bundleName);
            }
        }

        /// <summary>
        /// 移除AssetBundle名称
        /// </summary>
        public void RemoveAssetBundle(string bundleName)
        {
            assetBundleNames.Remove(bundleName);
        }

        /// <summary>
        /// 检查是否包含指定的Bundle名称
        /// </summary>
        public bool ContainsBundleName(string bundleName)
        {
            return assetBundleNames.Contains(bundleName);
        }

        /// <summary>
        /// 清空所有AssetBundle名称
        /// </summary>
        public void ClearAll()
        {
            assetBundleNames.Clear();
        }

        /// <summary>
        /// 获取AssetBundle数量
        /// </summary>
        public int Count => assetBundleNames.Count;

        /// <summary>
        /// 移动元素位置
        /// </summary>
        public void MoveElement(int fromIndex, int toIndex)
        {
            if (fromIndex >= 0 && fromIndex < assetBundleNames.Count &&
                toIndex >= 0 && toIndex < assetBundleNames.Count)
            {
                string item = assetBundleNames[fromIndex];
                assetBundleNames.RemoveAt(fromIndex);
                assetBundleNames.Insert(toIndex, item);
            }
        }

        /// <summary>
        /// 交换两个元素的位置
        /// </summary>
        public void SwapElements(int index1, int index2)
        {
            if (index1 >= 0 && index1 < assetBundleNames.Count &&
                index2 >= 0 && index2 < assetBundleNames.Count)
            {
                (assetBundleNames[index1], assetBundleNames[index2]) = (assetBundleNames[index2], assetBundleNames[index1]);
            }
        }

        /// <summary>
        /// 在指定位置插入AssetBundle名称
        /// </summary>
        public void InsertAssetBundle(int index, string bundleName)
        {
            if (!assetBundleNames.Contains(bundleName))
            {
                if (index >= 0 && index <= assetBundleNames.Count)
                {
                    assetBundleNames.Insert(index, bundleName);
                }
                else
                {
                    assetBundleNames.Add(bundleName);
                }
            }
        }
    }
}