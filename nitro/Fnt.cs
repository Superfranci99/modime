﻿//-----------------------------------------------------------------------
// <copyright file="Fnt.cs" company="none">
// Copyright (C) 2013
//
//   This program is free software: you can redistribute it and/or modify
//   it under the terms of the GNU General Public License as published by 
//   the Free Software Foundation, either version 3 of the License, or
//   (at your option) any later version.
//
//   This program is distributed in the hope that it will be useful, 
//   but WITHOUT ANY WARRANTY; without even the implied warranty of
//   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//   GNU General Public License for more details. 
//
//   You should have received a copy of the GNU General Public License
//   along with this program.  If not, see "http://www.gnu.org/licenses/". 
// </copyright>
// <author>pleoNeX</author>
// <email>benito356@gmail.com</email>
// <date>26/02/2013</date>
//-----------------------------------------------------------------------
namespace Nitro
{
    using System;
    using System.Collections.Generic;
    using System.Text;
	using Libgame;
    
    /// <summary>
    /// File Name Table
    /// </summary>
	public sealed class Fnt : Format
    {
        private const int FntEntrySize = 0x08;
        private static readonly Encoding DefaultEncoding = Encoding.GetEncoding("shift_jis");
        
        private Fnt.FntTable[] tables;
                          
		public override string FormatName {
			get { return "Nitro.FNT"; }
		}
		   
		public override void Initialize(GameFile file, params object[] parameters)
		{
			base.Initialize(file, parameters);

			if (parameters.Length == 2) {
				this.CreateTables((GameFolder)parameters[0], (int)parameters[1]);
			}
		}
		  
        /// <summary>
        /// Write a <see cref="Fnt" /> section in a stream.
        /// </summary>
        /// <param name="str">Stream to write to.</param>
		public override void Write(DataStream str)
        {
			DataWriter dw = new DataWriter(str);
            
            // Write main tables
			foreach (Fnt.FntTable table in this.tables) {
                dw.Write(table.Offset);
                dw.Write(table.IdFirstFile);
                dw.Write(table.IdParentFolder);
            }
            
            // Write subtables
            foreach (Fnt.FntTable table in this.tables)
                WriteSubTable(str, table);
        }
        
        /// <summary>
        /// Create the folder tree.
        /// </summary>
        /// <param name="files">Files in the tree.</param>
        /// <returns>Root folder</returns>
		public GameFolder CreateTree(GameFile[] files)
        {
			GameFolder root = new GameFolder("ROM");
			root.Tags["Id"] =  this.tables.Length.ToString();
            this.CreateTree(root, files);
            return root;
        }
        
        /// <summary>
        /// Read a FNT section from a stream.
        /// </summary>
        /// <param name="str">Stream to read from.</param>
		public override void Read(DataStream str)
        {
			DataReader dr = new DataReader(str);
			uint fntOffset = (uint)str.Position;

            // Get the number of directories and the offset to subtables
            //  from the main table.
			uint subtablesOffset = dr.ReadUInt32() + fntOffset;
            dr.ReadUInt16();
            ushort numDirs = dr.ReadUInt16();

            this.tables = new Fnt.FntTable[numDirs];
			for (int i = 0; i < numDirs; i++) {
				str.Seek(fntOffset + (i * FntEntrySize), SeekMode.Origin);
                
                // Error, in some cases the number of directories is wrong.
                // Found in FF Four Heroes of Light, Tetris Party deluxe.
				if (str.Position > subtablesOffset) {
                    numDirs = (ushort)i;
                    Array.Resize(ref this.tables, numDirs);
                    break;
                }
                
                FntTable table = new FntTable();
                table.Offset = dr.ReadUInt32();
                table.IdFirstFile = dr.ReadUInt16();
                table.IdParentFolder = dr.ReadUInt16();

                // Read subtable
				str.Seek(fntOffset + table.Offset, SeekMode.Origin);
                ReadSubTable(str, table);
            
                this.tables[i] = table;
            }
        }
        
		private static void ReadSubTable(DataStream str, FntTable table)
        {
			DataReader dr = new DataReader(str, EndiannessMode.LittleEndian, DefaultEncoding);
            
            byte nodeType = dr.ReadByte();
            ushort fileId = table.IdFirstFile;
                
            int nameLength;
            string name;
            
            // Read until the end of the subtable (reachs 0x00)
            while (nodeType != 0x0)
            {
                // If the node is a file.
                if (nodeType < 0x80)
                {
                    nameLength = nodeType;
					name = dr.ReadString(nameLength);
            
                    table.AddFileInfo(name, fileId++);
                }
                else
                {
                    nameLength = nodeType - 0x80;
					name = dr.ReadString(nameLength);
                    ushort folderId = dr.ReadUInt16();
    
                    table.AddFolderInfo(name, folderId);
                }
    
                nodeType = dr.ReadByte();
            }
        }
        
		private static void WriteSubTable(DataStream str, FntTable table)
        {
			DataWriter bw = new DataWriter(str);
            
            byte nodeType;
            
            // Write file info
            foreach (ElementInfo info in table.Files)
            {
                nodeType = (byte)DefaultEncoding.GetByteCount(info.Name); // Name length
                bw.Write(nodeType);
                bw.Write(DefaultEncoding.GetBytes(info.Name));
            }
            
            // Write folder info
            foreach (ElementInfo info in table.Folders)
            {
                nodeType = (byte)(0x80 | DefaultEncoding.GetByteCount(info.Name));
                bw.Write(nodeType);
                bw.Write(DefaultEncoding.GetBytes(info.Name));
                bw.Write(info.Id);
            }
            
            bw.Write((byte)0x00);   // End of info
            bw = null;
        }
        
		private static int CountDirectories(GameFolder folder)
        {
            int numDirs = folder.Folders.Count;
            
			foreach (GameFolder subfolder in folder.Folders)
                numDirs += CountDirectories(subfolder);
            
            return numDirs;
        }
        
		private static int ReassignFileIds(GameFolder folder, int currentId)
        {
			foreach (GameFile file in folder.Files)
				file.Tags["Id"] = (currentId++).ToString();
            
			foreach (GameFolder subfolder in folder.Folders)
                currentId = ReassignFileIds(subfolder, currentId);
            
            return currentId;
        }
        
		private static ushort GetIdFirstFile(GameFolder folder)
        {
            ushort id = 0xFFFF;
            
            // Searchs in all the files
			foreach (GameFile file in folder.Files)
            {
				if (int.Parse(file.Tags["Id"]) < id)
                {
					id = ushort.Parse(file.Tags["Id"]);
                }
            }
            
            // Searchs in subfolders
			foreach (GameFolder subfolder in folder.Folders)
            {
                ushort fId = GetIdFirstFile(subfolder);
                if (fId < id)
                {
                    id = fId;
                }
            }
            
            return id;
        }
        
		private void CreateTree(GameFolder currentFolder, GameFile[] listFile)
        {
			int folderId = (int.Parse(currentFolder.Tags["Id"]) > 0x0FFF) ?
			                int.Parse(currentFolder.Tags["Id"]) & 0x0FFF : 0;
            
            // Add files
            foreach (ElementInfo fileInfo in this.tables[folderId].Files)
            {
				listFile[fileInfo.Id].Name = fileInfo.Name;
                currentFolder.AddFile(listFile[fileInfo.Id]);
            }
            
            // Add subfolders
            foreach (ElementInfo folderInfo in this.tables[folderId].Folders)
            {
				GameFolder subFolder = new GameFolder(folderInfo.Name);
				subFolder.Tags["Id"] =  folderInfo.Id.ToString();
                this.CreateTree(subFolder, listFile);
				currentFolder.AddFolder(subFolder);
            }
        }
        
		private void CreateTables(GameFolder root, int firstId)
        {
            int numDirs = CountDirectories(root) + 1;
            this.tables = new Fnt.FntTable[numDirs];

            ReassignFileIds(root, firstId);
            
            // For each directory create its table.
            uint subtableOffset = (uint)(this.tables.Length * Fnt.FntTable.MainTableSize);
            this.CreateTablesRecur(root, (ushort)numDirs, ref subtableOffset);
        }
        
		private void CreateTablesRecur(GameFolder currentFolder, ushort parentId, ref uint subtablesOffset)
        {
			int folderId = (int.Parse(currentFolder.Tags["Id"]) > 0x0FFF) ?
			                int.Parse(currentFolder.Tags["Id"]) & 0x0FFF : 0;
            
            this.tables[folderId] = new Fnt.FntTable();
            this.tables[folderId].Offset = subtablesOffset;
            this.tables[folderId].IdParentFolder = parentId;
            this.tables[folderId].IdFirstFile = GetIdFirstFile(currentFolder);
            
            // Set the info values
			foreach (GameFile file in currentFolder.Files)
            {
				this.tables[folderId].AddFileInfo(file.Name, ushort.Parse(file.Tags["Id"]));
            }
            
			foreach (GameFolder folder in currentFolder.Folders)
            {
				this.tables[folderId].AddFolderInfo(folder.Name, ushort.Parse(folder.Tags["Id"]));
            }
            
            subtablesOffset += (uint)this.tables[folderId].GetInfoSize();
            
			foreach (GameFolder folder in currentFolder.Folders)
            {
                this.CreateTablesRecur(folder, (ushort)(0xF000 | folderId), ref subtablesOffset);
            }
        }
        
		public override void Export(DataStream strOut)
		{
			throw new NotImplementedException();
		}

		public override void Import(DataStream strIn)
		{
			throw new NotImplementedException();
		}

        private struct ElementInfo
        {
            public string Name { get; set; }
            
            public ushort Id { get; set; }
        }
        
        /// <summary>
        /// Substructure inside of the File Name Table.
        /// </summary>
        private class FntTable
        {
            private uint offset;
            private ushort idFirstFile;
            private ushort idParentFolder;
            private List<ElementInfo> folders;
            private List<ElementInfo> files;
            
            /// <summary>
            /// Initializes a new instance of the <see cref="FntTable" /> class.
            /// </summary>
            public FntTable()
            {
                this.folders = new List<Fnt.ElementInfo>();
                this.files = new List<Fnt.ElementInfo>();
            }
            
            /// <summary>
            /// Gets the size of the main table data.
            /// </summary>
            public static int MainTableSize
            {
                get { return 0x08; }
            }
            
            /// <summary>
            /// Gets or sets the relative offset to the folder info.
            /// </summary>
            public uint Offset 
            {
                get { return this.offset; }
                set { this.offset = value; }
            }

            /// <summary>
            /// Gets or sets the id of the first file in this folder (or in its subfolders).
            /// </summary>
            public ushort IdFirstFile
            {
                get { return this.idFirstFile; }
                set { this.idFirstFile = value; }
            }

            /// <summary>
            /// Gets or sets the id of the parent folder.
            /// </summary>
            public ushort IdParentFolder
            {
                get { return this.idParentFolder; }
                set { this.idParentFolder = value; }
            }

            /// <summary>
            /// Gets the folders of that table.
            /// </summary>
            public List<ElementInfo> Folders 
            {
                get { return this.folders; }
            }
            
            /// <summary>
            /// Gets the files of that table.
            /// </summary>
            public List<ElementInfo> Files
            {
                get { return this.files; }
            }
            
            /// <summary>
            /// Add the info of a new file of that table.
            /// </summary>
            /// <param name="name">File name</param>
            /// <param name="id">File ID</param>
            public void AddFileInfo(string name, ushort id)
            {
                this.files.Add(new ElementInfo() { Name = name, Id = id });
            }
            
            /// <summary>
            /// Add the info of a new folder of that table.
            /// </summary>
            /// <param name="name">Folder name</param>
            /// <param name="id">Folder ID</param>
            public void AddFolderInfo(string name, ushort id)
            {
                this.folders.Add(new ElementInfo() { Name = name, Id = id });
            }
            
            /// <summary>
            /// Gets the size of the table info.
            /// </summary>
            /// <returns>Size of the table info.</returns>
            public int GetInfoSize()
            {
                int size = 1;   // End node type
                
                foreach (ElementInfo info in this.files)
                {
                    size += 1;  // Node type
                    size += DefaultEncoding.GetByteCount(info.Name);
                }
                
                foreach (ElementInfo info in this.folders)
                {
                    size += 3;  // Node type + folder ID
                    size += DefaultEncoding.GetByteCount(info.Name);
                }
                
                return size;
            }
        }
    }
}