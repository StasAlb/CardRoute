using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Xml;
using HugeLib;

namespace FirstPoint
{
    class RecordClass
    {
        private string originalString;
        public string OriginalString
        {
            get
            {
                return originalString;
            }
            set
            {
                originalString = value;
            }
        }

        private string fileName;
        public string FileName
        {
            get { return fileName; }
            set { fileName = value; }
        }
        private Hashtable fields = new Hashtable();
        private string advanceString = "";
        public string AdvanceString
        {
            get
            {
                return advanceString;
            }
        }

        public string GetField(string fieldName)
        {
            return (fields.ContainsKey(fieldName)) ? (string)fields[fieldName] : "";
        }
        public void ParseFields(XmlDocument xmlFormat)
        {
            fields.Clear();
            advanceString = "";
            int cnt = XmlClass.GetXmlNodeCount(xmlFormat, "InputFormat/InputFields", null);
            //List<string> alist = new List<string>();
            for (int i = 0; i < cnt; i++)
            {
                XmlDocument xd = XmlClass.GetXmlNode(xmlFormat, "InputFormat/InputFields", i, null);
                string name = XmlClass.GetAttribute(xd, "", "Name", null);
                string tp = XmlClass.GetTag(xd, "Type", null);
                string value = "";
                switch (tp)
                {
                    case "ArrayItem":
                        string sep = XmlClass.GetTag(xd, "Separator", null);
                        string index = XmlClass.GetTag(xd, "Index", null);
                        try
                        {
                            value = originalString.Split(sep[0])[Convert.ToInt32(index)];
                        }
                        catch
                        {
                            return; //чтобы не попал в хэштаблицу с пустым значением
                        }
                        break;
                    case "FixedLength":
                        string start = XmlClass.GetTag(xd, "Start", null);
                        string length = XmlClass.GetTag(xd, "Length", null);
                        try
                        {
                            value = originalString.Substring(Convert.ToInt32(start), Convert.ToInt32(length));
                        }
                        catch
                        {
                            value = "";
                        }
                        break;
                    case "FileName":
                        value = fileName;
                        break;
                    default:
                        value = "";
                        break;
                }
                #region AddOn functions
                string function = XmlClass.GetAttribute(xd, "AddOn", "Function", null);
                switch (function)
                {
                    case "Substring":
                        string sStart = XmlClass.GetAttribute(xd, "AddOn", "Start", "0", null);
                        string sLength = XmlClass.GetAttribute(xd, "AddOn", "Length", "0", null); //0 до конца
                        int iStart = 0, iLength = 0;
                        try
                        {
                            iStart = Convert.ToInt32(sStart);
                            iLength = Convert.ToInt32(sLength);
                        }
                        catch { break; }
                        try
                        {
                            if (iLength == 0)
                                value = value.Substring(iStart);
                            else
                                value = value.Substring(iStart, iLength);
                        }
                        catch { }
                        break;
                    default:
                        break;
                }
                #endregion
                if (fields.ContainsKey(name))
                    fields[name] = value;
                else
                    fields.Add(name, value);
            }
        }
        public bool CheckCondition(string fieldName, string condition, string value)
        {
            if (!fields.ContainsKey(fieldName))
                return false;
            switch (condition)
            {
                case "Equal":
                    return (value == (string)fields[fieldName]);
                case "NotEqual":
                    return (value != (string)fields[fieldName]);
                case "Contain":
                    return (((string)fields[fieldName]).IndexOf(value) >= 0);
                case "NotContain":
                    return (((string)fields[fieldName]).IndexOf(value) < 0);
                case "StartsWith":
                    return (((string)fields[fieldName]).StartsWith(value));
                case "EndWith":
                    return (((string)fields[fieldName]).EndsWith(value));
                case "ItemOfCSV":
                    return value.Split(',').Contains((string)fields[fieldName]);
                default:
                    throw new Exception($"unknown condition: {condition}");
            }
        }
    }

    class ProductClass
    {
        public string ProductName;

        public List<List<Condition>> conditions;

        public string AdditionalXml;
        /// <summary>
        /// игнорировать данный продукт при отображении и обработке (для технических строк во входном файле)
        /// </summary>
        public bool Ignore;
    }

    struct Condition
    {
        public string Field;
        public string Type;
        public string Value;
    }
}
