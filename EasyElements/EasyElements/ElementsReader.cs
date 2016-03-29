﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Configuration;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EasyElements.Configs;

namespace EasyElements
{
    public class ElementsReader : IElementsReader
    {
        public string PathElements { get; }
        public ElementsData ElementsData { get; private set; }

        private readonly Config _config;
        private short version;

        public ElementsReader(string pathElements, string pathToConfigs)
        {
            if (string.IsNullOrEmpty(pathToConfigs))
                throw new ArgumentException("Argument is null or empty", nameof(pathToConfigs));

            this.PathElements = pathElements;
            this._config = new ConfigReader(pathToConfigs).Open();
        }

        public ElementsReader(string pathElements, Config config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            this.PathElements = pathElements;
            this._config = config;
        }

        public ElementsData Open()
        {
            if (string.IsNullOrEmpty(PathElements))
                throw new ArgumentException("Argument is null or empty", nameof(PathElements));

            if (!File.Exists(PathElements))
                throw new FileNotFoundException(PathElements);

            var stopwatch = Stopwatch.StartNew();

            using (var br = new BinaryReader(File.OpenRead(PathElements)))
                Read(br);

            stopwatch.Stop();
            Debug.Print($"Open the elements.data in {stopwatch.Elapsed} second");

            return ElementsData;
        }

        private void Read(BinaryReader br)
        {
            version = br.ReadInt16();
            var segmentation = br.ReadInt16();
            var dataSet = new DataSet();
            var skipValues = new Dictionary<ElementsList, List<byte[]>>();
            var CurrentConfig = _config.Lists.Where(x => x.Version <= version).ToList();

            foreach (var list in CurrentConfig)
            {
                if (list.Skip != "0")
                    skipValues.Add(list, ReadSkip(br, list));
                
                dataSet.Tables.Add(NewTable(br, list));
            }

            ElementsData = new ElementsData(version, segmentation, dataSet, skipValues, CurrentConfig);

        }

        private DataTable NewTable(BinaryReader br, ElementsList list)
        {
            var table = new DataTable(list.Name);
            list.Types = list.Types.Where(x => x.Version <= version).ToList();

            foreach (var type in list.Types)
                table.Columns.Add(type.Name, type.GetNormalType());

            if (!list.Types.Any())
                return table;

            var length = br.ReadInt32();

            for (var i = 0; i < length; i++)
                table.Rows.Add(NewRow(br, table, list.Types));
            
            return table;
        }

        private DataRow NewRow(BinaryReader br, DataTable table, List<ElementsType> types)
        {
            var row = table.NewRow();
            var j = 0;

            foreach (var type in types)
            {
                switch (type.Type)
                {
                    case "int": row[j] = br.ReadInt32(); break;
                    case "float": row[j] = br.ReadSingle(); break;
                    case "string": row[j] = Encoding.GetEncoding(type.Encoding).GetString(br.ReadBytes(int.Parse(type.SizeString))); break;
                    default: throw new ArgumentOutOfRangeException();
                }

                j++;
            }
            return row;
        }

        private List<byte[]> ReadSkip(BinaryReader br, ElementsList list)
        {
            var vals = new List<byte[]>();
            switch (list.Skip)
            {
                case "AUTO":
                    vals.Add(br.ReadBytes(4));
                    var count = br.ReadInt32();
                    vals.Add(BitConverter.GetBytes(count));
                    vals.Add(br.ReadBytes(count));
                    count = br.ReadInt32();
                    while (count <= 0 || count > 10000)
                    {
                        vals.Add(BitConverter.GetBytes(count));
                        count = br.ReadInt32();
                    }
                    br.BaseStream.Position -= 4;
                    break;
                case "RAW":
                    var rCount = br.ReadInt32();
                    vals.Add(BitConverter.GetBytes(rCount));
                    for (var i = 0; i < rCount; i++)
                    {
                        vals.Add(br.ReadBytes(132));
                        var count2 = br.ReadInt32();
                        vals.Add(BitConverter.GetBytes(count2));
                        for (var i2 = 0; i2 < count2; i2++)
                        {
                            vals.Add(br.ReadBytes(8));
                            var l = br.ReadInt32();
                            vals.Add(BitConverter.GetBytes(l));
                            vals.Add(br.ReadBytes(l * 2));
                            var count3 = br.ReadInt32();
                            vals.Add(BitConverter.GetBytes(count3));
                            vals.Add(br.ReadBytes(count3 * 136));
                        }
                    }
                    break;
                default:
                    vals.Add(br.ReadBytes(int.Parse(list.Skip)));
                    break;
            }
            return vals;
        }
    }
}