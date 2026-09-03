using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Xml.Linq;

namespace HKX2
{
    public class XmlSerializer
    {
        private int _index = 0050;
        private HKXHeader _header;
        private Dictionary<IHavokObject, string> _serializedObjectsLookup;
        private XDocument _document;
        private XContainer _dataSection;

        private string GetIndex()
        {
            return "#" + _index++.ToString("D4");
        }

        private static string FormatSignature(uint signature)
        {
            return "0x" + signature.ToString("x8");
        }

        public void Serialize(IHavokObject rootObject, HKXHeader header, Stream stream)
        {

            _header = header;
            _serializedObjectsLookup = new Dictionary<IHavokObject, string>(ReferenceEqualityComparer.Instance);

            var index = GetIndex();

            _document = new XDocument(
                new XDeclaration("1.0", "ascii", null),
                new XElement("hkpackfile",
                    new XAttribute("classversion", header.FileVersion),
                    new XAttribute("contentsversion", header.ContentsVersionString),
                    new XAttribute("toplevelobject", index),
                    new XElement("hksection",
                        new XAttribute("name", "__data__"))));

            _dataSection = _document.Element("hkpackfile").Element("hksection");

            var hkrootcontainer = WriteNode(rootObject, index);
            rootObject.WriteXml(this, hkrootcontainer);

            _document.Save(stream);
        }

        private XElement WriteNode<T>(T hkobject, string nodeName) where T : IHavokObject
        {
            XElement ele = new("hkobject",
                new XAttribute("name", nodeName),
                new XAttribute("class", hkobject.GetType().Name),
                new XAttribute("signature", FormatSignature(hkobject.Signature)));
            _dataSection.Add(ele);
            return ele;
        }

        public void WriteClassPointer<T>(XElement xe, string paramName, T? value) where T : IHavokObject
        {
            if (value is null)
            {
                WriteString(xe, paramName, "null");
                return;
            }
            if (_serializedObjectsLookup.ContainsKey(value))
            {
                var index = _serializedObjectsLookup[value];
                var hkparam = WriteString(xe, paramName, index);
            }
            else
            {
                var index = GetIndex();
                _serializedObjectsLookup.Add(value, index);
                WriteString(xe, paramName, index);
                var node = WriteNode(value, index);
                value.WriteXml(this, node);
            }
        }

        public void WriteClassPointerArray<T>(XElement xe, string paramName, IList<T?> values) where T : IHavokObject
        {
            var indexs = new List<string>();
            var hkparam = WriteParam(xe, paramName);
            hkparam.Add(new XAttribute("numelements", values.Count));
            foreach (var item in values)
            {
                if (item is null)
                {
                    indexs.Add("null");
                    continue;
                }
                if (_serializedObjectsLookup.ContainsKey(item))
                {
                    indexs.Add(_serializedObjectsLookup[item]);
                    continue;
                }
                var index = GetIndex();
                _serializedObjectsLookup.Add(item, index);
                indexs.Add(index);
                var node = WriteNode(item, index);
                item.WriteXml(this, node);
            }
            hkparam.Add(new XText(string.Join(" ", indexs)));
        }

        public void WriteClass<T>(XElement xe, string paramName, T value) where T : IHavokObject
        {
            var hkobject = new XElement("hkobject");
            WriteObject(xe, paramName, hkobject);
            value.WriteXml(this, hkobject);
        }

        public void WriteClassArray<T>(XElement xe, string paramName, IList<T> values) where T : IHavokObject
        {
            var hkparam = WriteParam(xe, paramName);
            hkparam.Add(new XAttribute("numelements", values.Count));
            foreach (var item in values)
            {
                var hkobject = new XElement("hkobject");
                hkparam.Add(hkobject);
                item.WriteXml(this, hkobject);
            }
        }


        public void WriteSerializeIgnored(XElement xe, string prop)
        {
            if (prop.StartsWith("m_"))
            {
                prop = prop[2..];
            }
            WriteComment(xe, prop + " SERIALIZE_IGNORED");
        }

        private static void WriteComment(XElement xe, string value)
        {
            xe.Add(new XComment(value));
        }

        private XElement WriteParam(XElement xe, string paramName, params object[] value)
        {
            if (paramName.StartsWith("m_"))
            {
                paramName = paramName[2..];
            }
            var hkparam = new XElement("hkparam", new XAttribute("name", paramName), value);
            xe.Add(hkparam);
            return hkparam;
        }

        private XElement WriteParam(XElement xe, string paramName)
        {
            return WriteParam(xe, paramName, "");
        }

        public XElement WriteObject(XElement xe, string paramName, XElement value)
        {
            return WriteParam(xe, paramName, value);
        }

        public XElement WriteBoolean(XElement xe, string paramName, bool value)
        {
            return WriteParam(xe, paramName, value ? "true" : "false");
        }

        public XElement WriteBooleanArray(XElement xe, string paramName, IList<bool> value)
        {
            var formated = value.Select(x => x ? "true" : "false")
                                .Chunk(16)
                                .Select(x => string.Join(" ", x));

            return WriteParam(xe, paramName, new XText(string.Join(" ", formated)), new XAttribute("numelements", value.Count));
        }

        public XElement WriteNumber<T>(XElement xe, string paramName, T value) where T : IBinaryInteger<T>
        {
            return WriteParam(xe, paramName, value);
        }

        public XElement WriteNumberArray<T>(XElement xe, string paramName, IList<T> value) where T : IBinaryInteger<T>
        {
            var formated = value.Chunk(16)
                                .Select(x => string.Join(" ", x));

            return WriteParam(xe, paramName, string.Join(" ", formated), new XAttribute("numelements", value.Count));
        }

        // Floats must be written round-trippably ("R") and in a fixed culture, so
        // that xml export is lossless and does not depend on the machine locale.
        private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);

        public XElement WriteFloat<T>(XElement xe, string paramName, T value) where T : IFloatingPoint<T>
        {
            return WriteParam(xe, paramName, value.ToString("R", CultureInfo.InvariantCulture));
        }

        public XElement WriteFloatArray<T>(XElement xe, string paramName, IList<T> value) where T : IFloatingPoint<T>
        {
            var formated = value.Select(x => x.ToString("R", CultureInfo.InvariantCulture))
                                .Chunk(16)
                                .Select(x => string.Join(" ", x));

            return WriteParam(xe, paramName, string.Join(" ", formated), new XAttribute("numelements", value.Count));
        }

        public XElement WriteVector4(XElement xe, string paramName, Vector4 value)
        {
            return WriteParam(xe, paramName, $"({F(value.X)} {F(value.Y)} {F(value.Z)} {F(value.W)})");
        }

        public XElement WriteVector4Array(XElement xe, string paramName, IList<Vector4> value)
        {
            var formated = value.Select(x => $"({F(x.X)} {F(x.Y)} {F(x.Z)} {F(x.W)})")
                                .Select(x => string.Join(" ", x));
            return WriteParam(xe, paramName, formated, new XAttribute("numelements", value.Count));
        }

        public XElement WriteQuaternion(XElement xe, string paramName, Quaternion value)
        {
            return WriteParam(xe, paramName, $"({F(value.X)} {F(value.Y)} {F(value.Z)} {F(value.W)})");
        }

        public XElement WriteQuaternionArray(XElement xe, string paramName, IList<Quaternion> value)
        {
            var formated = value.Select(x => $"({F(x.X)} {F(x.Y)} {F(x.Z)} {F(x.W)})")
                                .Select(x => string.Join(" ", x));
            return WriteParam(xe, paramName, formated, new XAttribute("numelements", value.Count));
        }

        public XElement WriteMatrix3(XElement xe, string paramName, Matrix4x4 value)
        {
            return WriteParam(xe, paramName, $"({F(value.M11)} {F(value.M12)} {F(value.M13)})({F(value.M21)} {F(value.M22)} {F(value.M23)})({F(value.M31)} {F(value.M32)} {F(value.M33)})");
        }

        public XElement WriteMatrix3Array(XElement xe, string paramName, IList<Matrix4x4> value)
        {
            var formated = value.Select(value => $"({F(value.M11)} {F(value.M12)} {F(value.M13)})({F(value.M21)} {F(value.M22)} {F(value.M23)})({F(value.M31)} {F(value.M32)} {F(value.M33)})")
                                .Select(x => string.Join(" ", x));
            return WriteParam(xe, paramName, formated, new XAttribute("numelements", value.Count));
        }

        public XElement WriteRotation(XElement xe, string paramName, Matrix4x4 value)
        {
            return WriteMatrix3(xe, paramName, value);
        }

        public XElement WriteRotationArray(XElement xe, string paramName, IList<Matrix4x4> value)
        {
            var formated = value.Select(value => $"({F(value.M11)} {F(value.M12)} {F(value.M13)})({F(value.M21)} {F(value.M22)} {F(value.M23)})({F(value.M31)} {F(value.M32)} {F(value.M33)})")
                                .Select(x => string.Join(" ", x));
            return WriteParam(xe, paramName, formated, new XAttribute("numelements", value.Count));
        }

        public XElement WriteQSTransform(XElement xe, string paramName, Matrix4x4 value)
        {
            return WriteParam(xe, paramName, $"({F(value.M11)} {F(value.M12)} {F(value.M13)})({F(value.M21)} {F(value.M22)} {F(value.M23)} {F(value.M24)})({F(value.M31)} {F(value.M32)} {F(value.M33)})");
        }

        public XElement WriteQSTransformArray(XElement xe, string paramName, IList<Matrix4x4> value)
        {
            var formated = value.Select(value => $"({F(value.M11)} {F(value.M12)} {F(value.M13)})({F(value.M21)} {F(value.M22)} {F(value.M23)} {F(value.M24)})({F(value.M31)} {F(value.M32)} {F(value.M33)})")
                                .Select(x => string.Join(" ", x));
            return WriteParam(xe, paramName, formated, new XAttribute("numelements", value.Count));
        }

        public XElement WriteMatrix4(XElement xe, string paramName, Matrix4x4 value)
        {
            // TODO: verify
            return WriteParam(xe, paramName, $"({F(value.M11)} {F(value.M12)} {F(value.M13)} {F(value.M14)})({F(value.M21)} {F(value.M22)} {F(value.M23)} {F(value.M24)})({F(value.M31)} {F(value.M32)} {F(value.M33)} {F(value.M34)})({F(value.M41)} {F(value.M42)} {F(value.M43)} {F(value.M44)})");
        }

        public XElement WriteMatrix4Array(XElement xe, string paramName, IList<Matrix4x4> value)
        {
            // TODO: verify
            var formated = value.Select(value => $"({F(value.M11)} {F(value.M12)} {F(value.M13)} {F(value.M14)})({F(value.M21)} {F(value.M22)} {F(value.M23)} {F(value.M24)})({F(value.M31)} {F(value.M32)} {F(value.M33)} {F(value.M34)})({F(value.M41)} {F(value.M42)} {F(value.M43)} {F(value.M44)})")
                                .Select(x => string.Join(" ", x));
            return WriteParam(xe, paramName, formated, new XAttribute("numelements", value.Count));
        }

        public XElement WriteTransform(XElement xe, string paramName, Matrix4x4 value)
        {
            return WriteParam(xe, paramName, $"({F(value.M11)} {F(value.M12)} {F(value.M13)})({F(value.M21)} {F(value.M22)} {F(value.M23)})({F(value.M31)} {F(value.M32)} {F(value.M33)})({F(value.M41)} {F(value.M42)} {F(value.M43)})");
        }

        public XElement WriteTransformArray(XElement xe, string paramName, IList<Matrix4x4> value)
        {
            var formated = value.Select(value => $"({F(value.M11)} {F(value.M12)} {F(value.M13)})({F(value.M21)} {F(value.M22)} {F(value.M23)})({F(value.M31)} {F(value.M32)} {F(value.M33)})({F(value.M41)} {F(value.M42)} {F(value.M43)})")
                                .Select(x => string.Join(" ", x));
            return WriteParam(xe, paramName, formated, new XAttribute("numelements", value.Count));
        }

        public XElement WriteString(XElement xe, string paramName, string? value)
        {
            return WriteParam(xe, paramName, value is null ? '\u2400' : value);
        }

        public XElement WriteStringArray(XElement xe, string paramName, IList<string> values)
        {
            var hkparam = WriteParam(xe, paramName);
            hkparam.Add(new XAttribute("numelements", values.Count));
            foreach (var item in values)
            {
                hkparam.Add(new XElement("hkcstring", item is null ? "\u2400" : item));
            }
            return hkparam;
        }

        public XElement WriteFlag<TEnum, TValue>(XElement xe, string paramName, TValue value) where TEnum : Enum where TValue : IBinaryInteger<TValue>
        {
            return WriteParam(xe, paramName, value.ToFlagString<TEnum, TValue>());
        }

        public XElement WriteEnum<TEnum, TValue>(XElement xe, string paramName, TValue value) where TEnum : Enum where TValue : IBinaryInteger<TValue>
        {

            return WriteParam(xe, paramName, value.ToEnumName<TEnum, TValue>());
        }

    }

}
