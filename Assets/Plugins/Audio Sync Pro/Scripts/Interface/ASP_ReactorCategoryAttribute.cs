using UnityEngine;

namespace TelePresent.AudioSyncPro
{
    [System.AttributeUsage(System.AttributeTargets.Class, Inherited = false)]
    public class ASP_ReactorCategoryAttribute : PropertyAttribute
    {
        public string Category { get; private set; }
        public string SubCategory { get; private set; }

        public ASP_ReactorCategoryAttribute(string category, string subCategory = "")
        {
            Category = category;
            SubCategory = subCategory;
        }

        public override string ToString()
        {
            if (!string.IsNullOrEmpty(SubCategory))
            {
                return $"{Category}/{SubCategory}";
            }
            return Category;
        }
    }
}
