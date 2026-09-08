// <copyright file=ValueTable company="Sumarte Technology">
// Copyright (c) 2024 All Rights Reserved
// </copyright>
// <author>jack.liu@sumarte.com</author>
// <date>2026/8/25 14:54:23</date>
// <summary></summary>
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace TestingSoftware.Protocols.Transforms
{
    /// <summary>
    /// The item of the value table.
    /// </summary>
    [XmlType(TypeName = "item")]
    public class ValueItem
    {
        /// <summary>
        /// The key of the item
        /// </summary>
        [XmlAttribute]
        public int Key { get; set; } = 0;

        /// <summary>
        /// The value of the item
        /// </summary>
        [XmlAttribute]
        public string Value { get; set; } = "";
    }

    /// <summary>
    /// The class define the all available values for the table.
    /// </summary>
    public class ValueTable : Transform<string>
    {
        /// <summary>
        /// The dictionary is used to improve the transform speed.
        /// </summary>
        private Dictionary<int, string> _forward = new Dictionary<int, string>();

        /// <summary>
        /// The dictionary is used to improve the transform speed.
        /// </summary>
        private Dictionary<string, int> _inverse = new Dictionary<string, int>();

        /// <summary>
        /// The xml serialize field.
        /// </summary>
        [XmlArray("Items")]
        public List<ValueItem> Items { get; set; } = new List<ValueItem>();

        /// <summary>
        /// Return the all available values.
        /// </summary>
        public ICollection<string> Values => _forward.Values;

        /// <summary>
        /// Transform the raw value to physical value.
        /// </summary>
        /// <param name="raw"></param>
        /// <returns></returns>
        public override string Foward(int raw)
        {
            string result = "N/A";

            if (_forward.ContainsKey(raw))
            {
                result = _forward[raw];
            }

            return result;
        }

        /// <summary>
        /// Transform the physical value to raw value.
        /// </summary>
        /// <param name="physical"></param>
        /// <returns></returns>
        public override int Inverse(string physical)
        {
            int result = 0;

            if (_inverse.ContainsKey(physical))
            {
                result = _inverse[physical];
            }

            return result;
        }

        /// <summary>
        /// Update forward and inverse hash map if the items has been changed.
        /// </summary>
        public override void Update()
        {
            // Update the forward and inverse hash map if the item is valid.
            if (Items != null && Items.Count > 0)
            {
                _forward.Clear();
                _inverse.Clear();

                foreach (var item in Items)
                {
                    _forward.Add(item.Key, item.Value);
                    _inverse.Add(item.Value, item.Key);
                }
            }
        }
    }
}
