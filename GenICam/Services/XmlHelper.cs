using System;
using System.Collections.Generic;
using System.Xml;

namespace GenICam
{
    /// <summary>
    /// this class helps Gvcp to read all the registers from XML file
    /// </summary>
    public class XmlHelper : IXmlHelper
    {
        #region XML Setup

        public IGenPort GenPort { get; }
        private string NamespaceName { get; } = "ns";
        private string NamespacePrefix { get; } = string.Empty;
        private XmlNamespaceManager XmlNamespaceManager { get; } = null;
        private XmlDocument XmlDocument { get; } = null;

        #endregion XML Setup

        /// <summary>
        /// the main method to read XML file
        /// </summary>
        /// <param name="registerDictionary">Register Dictionary</param>
        /// <param name="regisetrGroupDictionary">Register Group Dictionary</param>
        /// <param name="tagName">First Parent Tag Name</param>
        /// <param name="xmlDocument">XML File</param>
        public XmlHelper(string tagName, XmlDocument xmlDocument, IGenPort genPort)
        {
            XmlNode xmlRoot = xmlDocument.FirstChild.NextSibling;
            if (xmlRoot.Attributes != null)
            {
                XmlAttribute xmlns = xmlRoot.Attributes["xmlns"];
                if (xmlns != null)
                {
                    XmlNamespaceManager = new XmlNamespaceManager(xmlDocument.NameTable);
                    XmlNamespaceManager.AddNamespace(NamespaceName, xmlns.Value);
                    NamespacePrefix = $"{NamespaceName}:";
                }
            }
            XmlDocument = xmlDocument;
            GenPort = genPort;
            XmlNode categoryList = XmlDocument.DocumentElement.GetElementsByTagName("Category").Item(0);

            CategoryDictionary = new List<ICategory>();

            foreach (XmlNode category in categoryList.ChildNodes)
            {
                List<ICategory> list = GetAllCategoryFeatures(category);
                GenCategory genCategory = new GenCategory() { GroupName = category.InnerText, CategoryProperties = GetCategoryProperties(category) };
                genCategory.PFeatures = list;
                CategoryDictionary.Add(genCategory);
            }
        }

        public List<ICategory> CategoryDictionary { get; }

        #region GenIcam Getters

        private ICategory GetGenCategory(XmlNode node)
        {
            ICategory genCategory = null;

            switch (node.Name)
            {
                case nameof(CategoryType.StringReg):
                    genCategory = GetStringCategory(node);
                    break;

                case nameof(CategoryType.Enumeration):
                    genCategory = GetEnumerationCategory(node);
                    break;

                case nameof(CategoryType.Command):
                    genCategory = GetCommandCategory(node);
                    break;

                case nameof(CategoryType.Integer):
                    genCategory = GetIntegerCategory(node);
                    break;

                case nameof(CategoryType.Boolean):
                    genCategory = GetBooleanCategory(node);
                    break;

                case nameof(CategoryType.Float):
                    genCategory = GetFloatCategory(node);
                    break;

                default:
                    break;
            }

            return genCategory;
        }

        private List<ICategory> GetAllCategoryFeatures(XmlNode node)
        {
            List<ICategory> pFeatures = new List<ICategory>();
            ICategory category = GetGenCategory(node);

            if (category is null)
            {
                XmlNode pNode = LookForChildInsideAllParents(node, node.InnerText);

                if (pNode != null)
                {
                    category = GetGenCategory(pNode);
                }
                else
                {
                    pNode = node;
                }

                if (category is null)
                {
                    foreach (XmlNode childNode in pNode.ChildNodes)
                    {
                        pNode = LookForChildInsideAllParents(childNode, childNode.InnerText);
                        if (pNode != null)
                        {
                            category = GetGenCategory(pNode);
                            if (category is null)
                            {
                                category = new GenCategory() { GroupName = childNode.InnerText };
                                category.PFeatures = GetAllCategoryFeatures(pNode);
                            }
                        }
                        if (childNode.Name == "pFeature")
                        {
                            pFeatures.Add(category);
                        }
                    }
                }
                else
                {
                    if (pNode.Name == "pFeature")
                    {
                        pFeatures.Add(category);
                    }
                }
            }
            else
            {
                if (node.Name == "pFeature")
                {
                    pFeatures.Add(category);
                }
            }

            return pFeatures;
        }

        private ICategory GetFloatCategory(XmlNode xmlNode)
        {
            CategoryProperties categoryPropreties = GetCategoryProperties(xmlNode);

            Dictionary<string, IntSwissKnife> expressions = new Dictionary<string, IntSwissKnife>();
            IPValue pValue = null;
            double min = 0, max = 0, value = 0;
            long inc = 0;
            string unit = "";
            Representation representation = Representation.PureNumber;
            XmlNode pNode;
            foreach (XmlNode node in xmlNode.ChildNodes)
            {
                switch (node.Name)
                {
                    case "Value":
                        value = double.Parse(node.InnerText);

                        break;

                    case "Min":
                        min = double.Parse(node.InnerText);
                        break;

                    case "Max":
                        max = double.Parse(node.InnerText);
                        break;

                    case "pMin":
                        pNode = ReadPNode(xmlNode.ParentNode, node.InnerText);
                        expressions.Add(node.Name, GetIntSwissKnife(pNode));
                        break;

                    case "pMax":
                        pNode = ReadPNode(xmlNode.ParentNode, node.InnerText);
                        expressions.Add(node.Name, GetIntSwissKnife(pNode));
                        break;

                    case "Inc":
                        inc = long.Parse(node.InnerText);
                        break;

                    case "pValue":

                        pNode = ReadPNode(xmlNode.ParentNode, node.InnerText);
                        pValue = GetRegister(pNode) ?? GetIntSwissKnife(pNode);
                        break;

                    case "Representation":
                        representation = Enum.Parse<Representation>(node.InnerText);
                        break;

                    case "Unit":
                        unit = node.InnerText;
                        break;

                    default:
                        break;
                }
            }

            return new GenFloat(categoryPropreties, min, max, inc, IncMode.fixedIncrement, representation, value, unit, pValue, expressions);
        }

        private ICategory GetBooleanCategory(XmlNode xmlNode)
        {
            CategoryProperties categoryPropreties = GetCategoryProperties(xmlNode);

            Dictionary<string, IntSwissKnife> expressions = new Dictionary<string, IntSwissKnife>();

            IPValue pValue = null;
            if (xmlNode.SelectSingleNode(NamespacePrefix + "pValue", XmlNamespaceManager) is XmlNode pValueNode)
            {
                XmlNode pNode = ReadPNode(xmlNode.ParentNode, pValueNode.InnerText);
                pValue = GetRegister(pNode);
                if (pValue is null)
                {
                    pValue = GetIntSwissKnife(pNode);
                }
                //expressions.Add(pValueNode.Name, GetIntSwissKnife(pNode));
            }

            return new GenBoolean(categoryPropreties, pValue, null);
        }

        private ICategory GetEnumerationCategory(XmlNode xmlNode)
        {
            CategoryProperties categoryProperties = GetCategoryProperties(xmlNode);

            Dictionary<string, EnumEntry> entry = new Dictionary<string, EnumEntry>();
            foreach (XmlNode enumEntry in xmlNode.SelectNodes(NamespacePrefix + "EnumEntry", XmlNamespaceManager))
            {
                IIsImplemented isImplementedValue = null;
                XmlNode isImplementedNode = enumEntry.SelectSingleNode(NamespacePrefix + "pIsImplemented", XmlNamespaceManager);
                if (isImplementedNode != null)
                {
                    XmlNode isImplementedExpr = ReadPNode(xmlNode.ParentNode, isImplementedNode.InnerText);

                    isImplementedValue = GetRegister(isImplementedExpr) ?? (IIsImplemented)GetIntSwissKnife(isImplementedExpr);
                    if (isImplementedValue is null)
                    {
                        isImplementedValue = GetGenCategory(isImplementedExpr);
                    }
                }

                uint entryValue = uint.Parse(enumEntry.SelectSingleNode(NamespacePrefix + "Value", XmlNamespaceManager).InnerText);
                entry.Add(enumEntry.Attributes["Name"].Value, new EnumEntry(entryValue, isImplementedValue));
            }

            XmlNode enumPValue = xmlNode.SelectSingleNode(NamespacePrefix + "pValue", XmlNamespaceManager);
            XmlNode enumPValueNode = ReadPNode(enumPValue.ParentNode, enumPValue.InnerText);

            IPValue pValue = GetRegister(enumPValueNode) ?? GetIntSwissKnife(enumPValueNode);

            return new GenEnumeration(categoryProperties, entry, pValue);
        }

        private ICategory GetStringCategory(XmlNode xmlNode)
        {
            CategoryProperties categoryProperties = GetCategoryProperties(xmlNode);

            long address = 0;
            XmlNode addressNode = xmlNode.SelectSingleNode(NamespacePrefix + "Address", XmlNamespaceManager);
            if (addressNode != null)
            {
                if (addressNode.InnerText.StartsWith("0x"))
                {
                    address = long.Parse(addressNode.InnerText.Substring(2), System.Globalization.NumberStyles.HexNumber);
                }
                else
                {
                    address = long.Parse(addressNode.InnerText);
                }
            }
            ushort length = ushort.Parse(xmlNode.SelectSingleNode(NamespacePrefix + "Length", XmlNamespaceManager).InnerText);
            GenAccessMode accessMode = (GenAccessMode)Enum.Parse(typeof(GenAccessMode), xmlNode.SelectSingleNode(NamespacePrefix + "AccessMode", XmlNamespaceManager).InnerText);

            return new GenStringReg(categoryProperties, address, length, accessMode, GenPort);
        }

        private ICategory GetIntegerCategory(XmlNode xmlNode)
        {
            CategoryProperties categoryPropreties = GetCategoryProperties(xmlNode);

            long min = 0, max = 0, inc = 0, value = 0;
            string unit = "";
            Representation representation = Representation.PureNumber;
            XmlNode pNode;

            Dictionary<string, IntSwissKnife> expressions = new Dictionary<string, IntSwissKnife>();

            IPValue pValue = null;

            foreach (XmlNode node in xmlNode.ChildNodes)
            {
                switch (node.Name)
                {
                    case "Value":
                        value = long.Parse(node.InnerText);

                        break;

                    case "Min":
                        min = long.Parse(node.InnerText);
                        break;

                    case "Max":
                        max = long.Parse(node.InnerText);
                        break;

                    case "pMin":
                        pNode = ReadPNode(xmlNode.ParentNode, node.InnerText);
                        expressions.Add(node.Name, GetIntSwissKnife(pNode));
                        break;

                    case "pMax":
                        pNode = ReadPNode(xmlNode.ParentNode, node.InnerText);
                        expressions.Add(node.Name, GetIntSwissKnife(pNode));
                        break;

                    case "Inc":
                        inc = long.Parse(node.InnerText);
                        break;

                    case "pValue":

                        pNode = ReadPNode(xmlNode.ParentNode, node.InnerText);
                        pValue = GetRegister(pNode) ?? GetIntSwissKnife(pNode);

                        break;

                    case "Representation":
                        representation = Enum.Parse<Representation>(node.InnerText);
                        break;

                    case "Unit":
                        unit = node.InnerText;
                        break;

                    default:
                        break;
                }
            }

            return new GenInteger(categoryPropreties, min, max, inc, IncMode.fixedIncrement, representation, value, unit, pValue, expressions);
        }

        private ICategory GetCommandCategory(XmlNode xmlNode)
        {
            CategoryProperties categoryProperties = GetCategoryProperties(xmlNode);

            long commandValue = 0;
            XmlNode commandValueNode = xmlNode.SelectSingleNode(NamespacePrefix + "CommandValue", XmlNamespaceManager);
            if (commandValueNode != null)
            {
                commandValue = long.Parse(commandValueNode.InnerText);
            }

            XmlNode pValueNode = xmlNode.SelectSingleNode(NamespacePrefix + "pValue", XmlNamespaceManager);

            XmlNode pNode = ReadPNode(xmlNode.ParentNode, pValueNode.InnerText);

            IPValue pValue = GetRegister(pNode) ?? GetIntSwissKnife(pNode);

            return new GenCommand(categoryProperties, commandValue, pValue, null);
        }

        private IPValue GetRegister(XmlNode node)
        {
            IPValue register = null;
            switch (node.Name)
            {
                case nameof(RegisterType.Integer):
                    register = GetGenInteger(node);
                    break;

                case nameof(RegisterType.IntReg):
                    register = GetIntReg(node);
                    break;

                case nameof(RegisterType.MaskedIntReg):
                    register = GetMaskedIntReg(node);
                    break;

                case nameof(RegisterType.FloatReg):
                    register = GetFloatReg(node);
                    break;

                default:
                    break;
            }

            return register;
        }

        private IRegister GetFloatReg(XmlNode xmlNode)
        {
            Dictionary<string, IntSwissKnife> registers = new Dictionary<string, IntSwissKnife>();

            long address = 0;
            XmlNode addressNode = xmlNode.SelectSingleNode(NamespacePrefix + "Address", XmlNamespaceManager);
            if (addressNode != null)
            {
                if (addressNode.InnerText.StartsWith("0x"))
                {
                    address = long.Parse(addressNode.InnerText.Substring(2), System.Globalization.NumberStyles.HexNumber);
                }
                else
                {
                    address = long.Parse(addressNode.InnerText);
                }
            }

            ushort length = ushort.Parse(xmlNode.SelectSingleNode(NamespacePrefix + "Length", XmlNamespaceManager).InnerText);
            GenAccessMode accessMode = (GenAccessMode)Enum.Parse(typeof(GenAccessMode), xmlNode.SelectSingleNode(NamespacePrefix + "AccessMode", XmlNamespaceManager).InnerText);

            if (xmlNode.SelectSingleNode(NamespacePrefix + "pAddress", XmlNamespaceManager) is XmlNode pFeatureNode)
            {
                registers.Add(pFeatureNode.InnerText, GetIntSwissKnife(ReadPNode(xmlNode.ParentNode, pFeatureNode.InnerText)));
            }

            return new GenIntReg(address, length, accessMode, registers, GenPort);
        }

        private IPValue GetGenInteger(XmlNode xmlNode)
        {
            long value = 0;

            foreach (XmlNode node in xmlNode.ChildNodes)
            {
                switch (node.Name)
                {
                    case "Value":
                        value = long.Parse(node.InnerText);
                        break;

                    default:
                        break;
                }
            }

            return new GenInteger(value);
        }

        private IRegister GetIntReg(XmlNode xmlNode)
        {
            Dictionary<string, IntSwissKnife> expressions = new Dictionary<string, IntSwissKnife>();

            long address = 0;
            XmlNode addressNode = xmlNode.SelectSingleNode(NamespacePrefix + "Address", XmlNamespaceManager);
            if (addressNode != null)
            {
                if (addressNode.InnerText.StartsWith("0x"))
                {
                    address = long.Parse(addressNode.InnerText.Substring(2), System.Globalization.NumberStyles.HexNumber);
                }
                else
                {
                    address = long.Parse(addressNode.InnerText);
                }
            }

            ushort length = ushort.Parse(xmlNode.SelectSingleNode(NamespacePrefix + "Length", XmlNamespaceManager).InnerText);
            GenAccessMode accessMode = (GenAccessMode)Enum.Parse(typeof(GenAccessMode), xmlNode.SelectSingleNode(NamespacePrefix + "AccessMode", XmlNamespaceManager).InnerText);

            if (xmlNode.SelectSingleNode(NamespacePrefix + "pAddress", XmlNamespaceManager) is XmlNode pFeatureNode)
            {
                expressions.Add(pFeatureNode.InnerText, GetIntSwissKnife(ReadPNode(xmlNode.ParentNode, pFeatureNode.InnerText)));
            }

            return new GenIntReg(address, length, accessMode, expressions, GenPort);
        }

        private IRegister GetMaskedIntReg(XmlNode xmlNode)
        {
            Dictionary<string, IntSwissKnife> expressions = new Dictionary<string, IntSwissKnife>();

            long address = 0;
            XmlNode addressNode = xmlNode.SelectSingleNode(NamespacePrefix + "Address", XmlNamespaceManager);

            if (addressNode != null)
            {
                if (addressNode.InnerText.StartsWith("0x"))
                {
                    address = long.Parse(addressNode.InnerText.Substring(2), System.Globalization.NumberStyles.HexNumber);
                }
                else
                {
                    address = long.Parse(addressNode.InnerText);
                }
            }

            ushort length = ushort.Parse(xmlNode.SelectSingleNode(NamespacePrefix + "Length", XmlNamespaceManager).InnerText);
            GenAccessMode accessMode = Enum.Parse<GenAccessMode>(xmlNode.SelectSingleNode(NamespacePrefix + "AccessMode", XmlNamespaceManager).InnerText);

            if (xmlNode.SelectSingleNode(NamespacePrefix + "pAddress", XmlNamespaceManager) is XmlNode pFeatureNode)
            {
                expressions.Add(pFeatureNode.InnerText, GetIntSwissKnife(ReadPNode(xmlNode.ParentNode, pFeatureNode.InnerText)));
            }

            return new GenMaskedIntReg(address, length, accessMode, expressions, GenPort);
        }

        private IntSwissKnife GetIntSwissKnife(XmlNode xmlNode)
        {
            if (xmlNode.Name != "IntSwissKnife" && xmlNode.Name != "SwissKnife")
            {
                return null;
            }

            Dictionary<string, object> pVariables = new Dictionary<string, object>();

            string formula = string.Empty;

            foreach (XmlNode node in xmlNode.ChildNodes)
            {
                //child could be either pVariable or Formula
                switch (node.Name)
                {
                    case "pVariable":
                        //pVariable could be IntSwissKnife, SwissKnife, Integer, IntReg, Float, FloatReg,

                        object pVariable = null;
                        XmlNode pNode = ReadPNode(xmlNode.ParentNode, node.InnerText);
                        pVariable = pNode.Name switch
                        {
                            "IntSwissKnife" => GetIntSwissKnife(pNode),
                            "SwissKnife" => GetGenCategory(pNode),
                            _ => GetRegister(pNode)
                        };

                        if (pVariable is null)
                        {
                            pVariable = GetGenCategory(pNode);
                        }

                        pVariables.Add(node.Attributes["Name"].Value, pVariable);
                        break;

                    case "Formula":
                        formula = node.InnerText;
                        break;

                    default:
                        break;
                }
            }
            if (pVariables.Count == 0)
            {
            }
            return new IntSwissKnife(formula, pVariables);
        }

        /// <summary>
        /// Get Category Properties such as Name, AccessMode and Visibility
        /// </summary>
        /// <param name="xmlNode"></param>
        /// <returns></returns>
        private CategoryProperties GetCategoryProperties(XmlNode xmlNode)
        {
            if (xmlNode.Name == "pFeature")
            {
                xmlNode = LookForChildInsideAllParents(xmlNode, xmlNode.InnerText);
            }

            GenVisibility visibilty = GenVisibility.Beginner;
            string toolTip = "", description = "";
            bool isStreamable = false;
            string name = xmlNode.Attributes["Name"].Value;

            if (xmlNode.SelectSingleNode(NamespacePrefix + "Visibility", XmlNamespaceManager) is XmlNode visibilityNode)
            {
                visibilty = Enum.Parse<GenVisibility>(visibilityNode.InnerText);
            }
            if (xmlNode.SelectSingleNode(NamespacePrefix + "ToolTip", XmlNamespaceManager) is XmlNode toolTipNode)
            {
                toolTip = toolTipNode.InnerText;
            }

            if (xmlNode.SelectSingleNode(NamespacePrefix + "Description", XmlNamespaceManager) is XmlNode descriptionNode)
            {
                description = descriptionNode.InnerText;
            }

            XmlNode isStreamableNode = xmlNode.SelectSingleNode(NamespacePrefix + "Streamable", XmlNamespaceManager);

            if (isStreamableNode != null)
            {
                if (isStreamableNode.InnerText == "Yes")
                {
                    isStreamable = true;
                }
            }

            string rootName = "";

            if (xmlNode.ParentNode.Attributes["Comment"] != null)
            {
                rootName = xmlNode.ParentNode.Attributes["Comment"].Value;
            }

            return new CategoryProperties(rootName, name, toolTip, description, visibilty, isStreamable);
        }

        #endregion GenIcam Getters

        #region XML Mapping Helpers

        private XmlNode GetNodeByAttirbuteValue(XmlNode parentNode, string tagName, string value)
        {
            return parentNode.SelectSingleNode($"{NamespacePrefix}{tagName}[@Name='{value}']", XmlNamespaceManager);
        }

        private XmlNode ReadPNode(XmlNode parentNode, string pNode)
        {
            if (GetNodeByAttirbuteValue(parentNode, "Integer", pNode) is XmlNode integerNode)
            {
                return LookForChildInsideAllParents(integerNode, pNode);
            }
            else if (GetNodeByAttirbuteValue(parentNode, "IntReg", pNode) is XmlNode intRegNode)
            {
                return LookForChildInsideAllParents(intRegNode, pNode);
            }
            else if (GetNodeByAttirbuteValue(parentNode, "IntSwissKnife", pNode) is XmlNode intSwissKnifeNode)
            {
                return LookForChildInsideAllParents(intSwissKnifeNode, pNode);
            }
            else if (GetNodeByAttirbuteValue(parentNode, "SwissKnife", pNode) is XmlNode swissKnifeNode)
            {
                return LookForChildInsideAllParents(swissKnifeNode, pNode);
            }
            else if (GetNodeByAttirbuteValue(parentNode, "Float", pNode) is XmlNode floatNode)
            {
                return LookForChildInsideAllParents(floatNode, pNode);
            }
            else if (GetNodeByAttirbuteValue(parentNode, "Boolean", pNode) is XmlNode booleanNode)
            {
                return LookForChildInsideAllParents(booleanNode, pNode);
            }
            else if (GetNodeByAttirbuteValue(parentNode, "MaskedIntReg", pNode) is XmlNode maskedIntRegNode)
            {
                return LookForChildInsideAllParents(maskedIntRegNode, pNode);
            }
            else
            {
                if (parentNode.ParentNode != null)
                {
                    return ReadPNode(parentNode.ParentNode, pNode);
                }
                else
                {
                    return LookForChildInsideAllParents(parentNode.FirstChild, pNode);
                }
            }
        }

        private XmlNode LookForChildInsideAllParents(XmlNode xmlNode, string childName)
        {
            foreach (XmlNode parent in xmlNode.ParentNode.ChildNodes)
            {
                foreach (XmlNode child in parent.ChildNodes)
                {
                    if (child.Attributes != null)
                    {
                        if (child.Attributes["Name"] != null && child.Attributes["Name"].Value == childName)
                        {
                            return child;
                        }
                    }
                }
            }

            if (xmlNode.ParentNode.ParentNode != null)
            {
                return LookForChildInsideAllParents(xmlNode.ParentNode, childName);
            }
            else
            {
                foreach (XmlNode parent in XmlDocument.DocumentElement.ChildNodes)
                {
                    foreach (XmlNode child in parent.ChildNodes)
                    {
                        if (child.Attributes != null)
                        {
                            if (child.Attributes["Name"] != null && child.Attributes["Name"].Value == childName)
                            {
                                return child;
                            }
                        }
                    }
                }

                return null;
            }
        }

        #endregion XML Mapping Helpers
    }
}