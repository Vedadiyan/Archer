using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Archer.Core
{
    public readonly struct GroupByKey
    {
        public static readonly string KeyIdentifier = Guid.NewGuid().ToString();
        public string[] Keys { get; }
        public string KeyHash { get; }
        public object[] Values { get; }
        public string ValueHash { get; }
        public GroupByKey(string[] keys, object[] values)
        {
            Keys = keys;
            Values = values;
            var stringKeys = string.Join(":", keys);
            var stringValues = string.Join(":", values);
            MD5 md5 = MD5.Create();
            KeyHash = KeyIdentifier + BitConverter.ToString(md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(stringKeys))).Replace("-", "");
            ValueHash = BitConverter.ToString(md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(stringValues))).Replace("-", "");
        }

        public override bool Equals(object obj)
        {
            if (obj is GroupByKey)
            {
                GroupByKey groupByKey = (GroupByKey)obj;
                return groupByKey.KeyHash == KeyHash && groupByKey.ValueHash == ValueHash;
            }
            else
            {
                return false;
            }
        }
        public override int GetHashCode()
        {
            return HashCode.Combine(KeyHash);
        }
        public override string ToString()
        {
            return string.Join(",", Values);
        }
    }
    public class GroupBy
    {
        public Dictionary<GroupByKey, List<Dictionary<string, object>>> Current { get; private set; }
        private readonly List<Dictionary<string, object>> records;
        public string Key { get; }
        public GroupBy Previous { get; private set; }
        public GroupByKey PreviousKey { get; private set; }
        public GroupBy[] Next { get; private set; }
        public GroupBy(string key, List<Dictionary<string, object>> records)
        {
            this.Key = key;
            this.records = records;
            Current = new Dictionary<GroupByKey, List<Dictionary<string, object>>>();
        }
        public void Group()
        {
            string[] keys = Key.Split(',');
            foreach (var record in records)
            {
                object[] values = new object[keys.Length];
                for (int i = 0; i < keys.Length; i++)
                {
                    values[i] = record[keys[i].TrimStart().TrimEnd()];
                }
                if (Current.TryGetValue(new GroupByKey(keys, values), out List<Dictionary<string, object>> value))
                {
                    value.Add(record);
                }
                else
                {
                    Current.Add(new GroupByKey(keys, values), new List<Dictionary<string, object>> { record });
                }
            }
        }
        public void ThenBy(string key)
        {
            if (Next == null)
            {
                Next = new GroupBy[Current.Count];
                int c = 0;
                foreach (var i in Current)
                {
                    GroupBy thenBy = new GroupBy(key, i.Value);
                    thenBy.Group();
                    thenBy.Previous = this;
                    thenBy.PreviousKey = i.Key;
                    Next[c++] = thenBy;
                }
            }
            else
            {
                foreach (var i in Next)
                {
                    i.ThenBy(key);
                }

            }
        }
        public static GroupObject Format(GroupBy groupBy)
        {
            GroupObject groupObject = new GroupObject();
            groupObject.AddKey(groupBy.Key);
            if (groupBy.Current != null)
            {
                foreach (var i in groupBy.Current)
                {
                    var _groupObject = new GroupObject();
                    _groupObject.AddKey(string.Join(":", i.Key.Values));
                    groupObject.AddNextValue(_groupObject);
                }
            }
            if (groupBy.Next != null)
            {
                foreach (var i in groupBy.Next)
                {
                    var rfc = Format(i);
                    var find = groupObject.NextValues.FirstOrDefault(x => x.Key == string.Join(":", i.PreviousKey.Values));
                    if (find != null)
                    {
                        find.AddNextValue(rfc);
                    }
                    else
                    {
                        groupObject.AddNextValue(rfc);
                    }
                }
            }
            else
            {
                foreach (var i in groupBy.Current)
                {
                    var find = groupObject.NextValues.FirstOrDefault(x => x.Key == string.Join(":", i.Key));
                    find.LastValue = i.Value;
                }
            }
            return groupObject;
        }
    }
    public class FormatObject
    {
        private JArray jArray;
        private Dictionary<string, string> map;
        private List<string> exlcudes;
        private HashSet<string> keys;
        private int level;
        public FormatObject(Dictionary<string, string> map, List<string> excludes)
        {
            jArray = new JArray();
            this.map = map;
            this.exlcudes = excludes;
            keys = new HashSet<string>();
            level = 1;
        }
        private FormatObject(JArray jArray, HashSet<string> keys, int level)
        {
            this.jArray = jArray;
            this.keys = keys;
            this.level = level + 1;
        }
        public void Format(GroupBy groupBy)
        {
            if (groupBy.Current != null)
            {
                foreach (var i in groupBy.Current)
                {
                    var obj = new JObject();
                    //obj.Add(String.Join(":", i.Key.Keys), String.Join(":", i.Key.Values));
                    JProperty jProperty = new JProperty(i.Key.KeyHash, i.Key.ValueHash);
                    obj.Add(jProperty);
                    for (int x = 0; x < i.Key.Keys.Length; x++)
                    {
                        obj.Add(new JProperty(i.Key.Keys[x], new JValue(i.Key.Values[x])));
                        keys.Add(i.Key.Keys[x]);
                    }
                    obj.Add("GroupByLevel_" + level.ToString(), new JArray());
                    jArray.Add(obj);
                }
            }
            if (groupBy.Next != null)
            {
                foreach (var i in groupBy.Next)
                {
                    //var find = jArray.FirstOrDefault(x => x[String.Join(":", i.PreviousKey.Keys)].ToString() == String.Join(":", i.PreviousKey.Values));
                    var find = jArray.FirstOrDefault(x => x[i.PreviousKey.KeyHash].ToString() == i.PreviousKey.ValueHash);
                    FormatObject formatObject = new FormatObject((JArray)find["GroupByLevel_" + level.ToString()], keys, level);
                    formatObject.Format(i);
                    find["GroupByLevel_" + level.ToString()] = formatObject.jArray;
                }
            }
            else
            {
                foreach (var i in groupBy.Current)
                {
                    var find = jArray.FirstOrDefault(x => x[i.Key.KeyHash].ToString() == i.Key.ValueHash);
                    //var find = jArray.FirstOrDefault(x => x[String.Join(":", i.Key.Keys)].ToString() == String.Join(":", i.Key.Values));
                    var next = (JArray)find["GroupByLevel_" + level.ToString()];
                    foreach (var item in i.Value)
                    {
                        JObject value = new JObject();
                        foreach (var keyValuePair in item)
                        {
                            if (keys.FirstOrDefault(x => x == keyValuePair.Key) == null)
                            {
                                value.Add(keyValuePair.Key, new JValue(keyValuePair.Value));
                            }
                        }
                        next.Add(value);
                    }
                }
            }
        }
        public void Format(List<Dictionary<string, object>> records)
        {
            foreach (var r in records)
            {
                JObject jObject = new JObject();
                foreach (var i in r)
                {
                    jObject.Add(new JProperty(i.Key, new JValue(i.Value)));
                }
                jArray.Add(jObject);
            }
        }
        private void correct(JToken token)
        {
            if (token != null && token.HasValues && token.Type == JTokenType.Property)
            {
                JProperty jProperty = ((JProperty)token);
                var ignore = jProperty.Name.StartsWith(GroupByKey.KeyIdentifier);
                if (exlcudes != null && (ignore || exlcudes.Contains(jProperty.Name)))
                {
                    token.Remove();
                }
                else
                {
                    if (map != null && map.TryGetValue(jProperty.Name, out string key))
                    {
                        jProperty = new JProperty(key, jProperty.Value);
                        token.Replace(jProperty);
                    }
                }
            }
        }
        private void correction(JToken token)
        {
            if (token != null)
            {
                correct(token);
                var children = token.Children();
                for (int i = 0; i < children.Count(); i++)
                {
                    var element = children.ElementAt(i);
                    correct(element);
                    if (i < children.Count())
                    {
                        correction(children.ElementAt(i));
                    }
                }
            }
        }
        public string GetJSON(bool ignoreFormat = false)
        {
            if (ignoreFormat == false)
            {
                correction(jArray);
            }
            StringBuilder sb = new StringBuilder();
            sb.Append("[").Append(string.Join(',', jArray.Where(x => x != null).Select(x => x.ToString(Newtonsoft.Json.Formatting.None)))).Append("]");
            return sb.ToString();
        }
        public string GetJSON(JToken jToken)
        {
            correction(jToken);
            return jToken.ToString();
        }
    }
    public class GroupObject
    {
        public string Key { get; private set; }
        public List<GroupObject> NextValues { get; private set; }
        public List<Dictionary<string, object>> LastValue { get; set; }
        public GroupObject()
        {
            NextValues = new List<GroupObject>();
        }
        public void AddKey(string key)
        {
            Key = key;
        }
        public void AddNextValue(GroupObject value)
        {
            NextValues.Add(value);
        }
    }
    public class FlattenedValue
    {
        public string Key { get; set; }
        public string Value { get; set; }
        public List<Dictionary<string, object>> Values { get; set; }
    }
}