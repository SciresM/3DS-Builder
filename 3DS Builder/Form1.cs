using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Media;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using blz;
using _3DS_Builder.Properties;

namespace _3DS_Builder
{

    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
            B_Go.Enabled = false;
            CB_Logo.Items.AddRange(new[] { "Nintendo", "Distributed", "Licensed", "iQue", "iQueForSystem" });
            CB_Logo.SelectedIndex = 0;
            RecognizedGames = new Dictionary<ulong, string[]>();
            string[] lines = Resources.ResourceManager.GetString("_3dsgames").Split('\n').ToArray();
            foreach (string l in lines)
            {
                string[] vars = l.Split('	').ToArray();
                ulong titleid = Convert.ToUInt64(vars[0], 16);
                if (RecognizedGames.ContainsKey(titleid))
                {
                    char lc = RecognizedGames[titleid].ToArray()[0].ToCharArray()[3];
                    char lc2 = vars[1].ToCharArray()[3];
                    if (lc2 == 'A' || lc2 == 'E' || (lc2 == 'P' && lc == 'J')) //Prefer games in order US, PAL, JP
                    {
                        RecognizedGames[titleid] = vars.Skip(1).Take(2).ToArray();
                    }
                }
                else
                {
                    RecognizedGames.Add(titleid, vars.Skip(1).Take(2).ToArray());
                }
            }
            CHK_Card2.Checked = true;
        }

        public volatile int threads = 0;

        private string EXEFS_PATH;
        private string ROMFS_PATH;
        private string EXHEADER_PATH;
        private string SAVE_PATH;
        public const uint MEDIA_UNIT_SIZE = 0x200;
        private string LOGO_NAME;
        private bool Card2;

        public static Dictionary<ulong, string[]> RecognizedGames;

        private NCSD Rom;

        private NCCH Content;

        private void B_Romfs_Click(object sender, EventArgs e)
        {
            if (threads > 0) { Alert("Please wait for all operations to finish first."); return; }

            if (CHK_PrebuiltRomfs.Checked)
            {
                OpenFileDialog ofd = new OpenFileDialog();
                if (ofd.ShowDialog() != DialogResult.OK) return;

                string magic;
                try
                {
                    using (BinaryReader br = new BinaryReader(File.OpenRead(ofd.FileName)))
                        magic = new string(br.ReadBytes(4).Select(c => (char)c).ToArray());
                }
                catch
                {
                    MessageBox.Show("Failed to read the provided file. Try again?");
                    return;
                }
                if (magic != "IVFC")
                {
                    MessageBox.Show("Provided file is not a valid romfs.");
                    return;
                }
                TB_Romfs.Text = ofd.FileName;
                Validate_Go();
            }
            else
            {
                FolderBrowserDialog fbd = new FolderBrowserDialog();
                if (fbd.ShowDialog() != DialogResult.OK) return;

                TB_Romfs.Text = fbd.SelectedPath;
                Validate_Go();
            }
        }
        private void B_Exefs_Click(object sender, EventArgs e)
        {
            if (threads > 0) { Alert("Please wait for all operations to finish first."); return; }
            if (CHK_PrebuiltExefs.Checked)
            {
                OpenFileDialog ofd = new OpenFileDialog();
                if (ofd.ShowDialog() != DialogResult.OK) return;

                TB_Exefs.Text = ofd.FileName;
                Validate_Go();
            }
            else
            {

                FolderBrowserDialog fbd = new FolderBrowserDialog();
                if (fbd.ShowDialog() != DialogResult.OK) return;

                string[] files = (new DirectoryInfo(fbd.SelectedPath)).GetFiles().Select(f => Path.GetFileNameWithoutExtension(f.FullName)).ToArray();
                if (((files.Contains("code") || files.Contains(".code")) && !(files.Contains(".code") && files.Contains("code"))) && files.Contains("banner") && files.Contains("icon") && files.Length < 10)
                {
                    FileInfo fi = (new DirectoryInfo(fbd.SelectedPath)).GetFiles()[Math.Max(Array.IndexOf(files, "code"), Array.IndexOf(files, ".code"))];
                    if (fi.Name == "code.bin")
                    {
                        Alert("Renaming \"code.bin\" to \".code.bin\"");
                        string newName = fi.DirectoryName + Path.DirectorySeparatorChar + ".code.bin";
                        File.Move(fi.FullName, newName);
                        fi = new FileInfo(newName);
                    }
                    if (fi.Length % 0x200 == 0)
                    {
                        if (Prompt(MessageBoxButtons.YesNo, "Detected Decompressed code.bin.", "Compress? File will be replaced. Do not build an ExeFS with an uncompressed code.bin if the Exheader doesn't specify it.") == DialogResult.Yes)
                        {
                            new Thread(() => { threads++; SetPrebuiltBoxes(false); new BLZCoder(new[] { "-en", fi.FullName }, PB_Show); SetPrebuiltBoxes(true); threads--; Alert("Compressed!"); }).Start();
                        }
                    }
                    if (files.Contains("logo"))
                    {
                        Alert("Deleting unneeded exefs logo binary.");
                        File.Delete((new DirectoryInfo(fbd.SelectedPath)).GetFiles()[Array.IndexOf(files, "logo")].FullName);
                    }
                    TB_Exefs.Text = fbd.SelectedPath;
                    Validate_Go();
                }
                else
                {
                    Alert("Your selected ExeFS is missing something essential.");
                }
            }
        }
        private void B_Exheader_Click(object sender, EventArgs e)
        {

            if (threads > 0) { Alert("Please wait for all operations to finish first."); return; }

            OpenFileDialog ofd = new OpenFileDialog();
            if (ofd.ShowDialog() == DialogResult.OK)
            {
                FileInfo fi = new FileInfo(ofd.FileName);
                if (fi.Length < 0x800)
                {
                    Alert("Selected Exheader is too short. Correct size is 0x800 for Exheader and AccessDescriptor.");
                    return;
                }
                TB_Exheader.Text = ofd.FileName;
                Exheader exh = new Exheader(TB_Exheader.Text);
                if (RecognizedGames.ContainsKey(exh.TitleID))
                {
                    if (Prompt(MessageBoxButtons.YesNo, "Detected " + RecognizedGames[exh.TitleID][1] + ". Load Defaults?") == DialogResult.Yes)
                        TB_Serial.Text = exh.GetSerial();
                }
            }
            Validate_Go();
        }

        private void B_SavePath_Click(object sender, EventArgs e)
        {
            if (threads > 0) { Alert("Please wait for all operations to finish first."); return; }

            SaveFileDialog sfd = new SaveFileDialog();
            if (sfd.ShowDialog() == DialogResult.OK)
                TB_SavePath.Text = sfd.FileName;

            Validate_Go();
        }

        private static DialogResult Alert(params string[] lines)
        {
            SystemSounds.Asterisk.Play();
            string msg = String.Join(Environment.NewLine + Environment.NewLine, lines);
            return MessageBox.Show(msg, "Alert", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private static DialogResult Prompt(MessageBoxButtons btn, params string[] lines)
        {
            SystemSounds.Question.Play();
            string msg = String.Join(Environment.NewLine + Environment.NewLine, lines);
            return MessageBox.Show(msg, "Prompt", btn, MessageBoxIcon.Asterisk);
        }

        private void Validate_Go()
        {
            bool isSerialValid = true;
            if (TB_Serial.Text.Length == 10)
            {
                string[] subs = TB_Serial.Text.Split('-');
                if (subs.Length != 3)
                    isSerialValid = false;
                else
                {
                    if (subs[0].Length != 3 || subs[1].Length != 1 || subs[2].Length != 4)
                        isSerialValid = false;
                    else if (subs[0] != "CTR" && subs[0] != "KTR")
                        isSerialValid = false;
                    else if (subs[1] != "P" && subs[1] != "N" && subs[2] != "U")
                        isSerialValid = false;
                    else
                    {
                        foreach (char c in subs[2])
                            if (!Char.IsLetterOrDigit(c))
                                isSerialValid = false;
                    }
                }
            }
            else
            {
                isSerialValid = false;
            }
            if (TB_Exefs.Text != string.Empty && TB_Romfs.Text != string.Empty && TB_Exheader.Text != string.Empty && TB_SavePath.Text != string.Empty && isSerialValid)
            {
                Exheader exh = new Exheader(TB_Exheader.Text);
                if (exh.isPokemon() && !Card2)
                {
                    Alert("Pokemon games should not be Card 1.");
                }
                else
                {
                    B_Go.Enabled = true;
                }
            }
            else
            {
                B_Go.Enabled = false;
            }
        }

        private void B_Go_Click(object sender, EventArgs e)
        {
            if (threads > 0) { Alert("Please wait for all operations to finish first."); return; }
            EXEFS_PATH = TB_Exefs.Text;
            ROMFS_PATH = TB_Romfs.Text;
            EXHEADER_PATH = TB_Exheader.Text;
            SAVE_PATH = TB_SavePath.Text;
            new Thread(() => { threads++; SetPrebuiltBoxes(false); BuildNCSD(); SetPrebuiltBoxes(true); threads--; }).Start();
            B_Exefs.Enabled = B_Romfs.Enabled = B_Exheader.Enabled = B_SavePath.Enabled = CHK_Card2.Enabled = TB_Serial.Enabled = B_Go.Enabled = CB_Logo.Enabled = false;
        }

        private void BuildNCSD()
        {
            Rom = new NCSD { NCCH_Array = new List<NCCH>() };
            BuildNCCH(); //Build Content NCCH
            Rom.NCCH_Array.Add(Content);
            TB_Progress.Invoke((Action)(() => UpdateTB_Progress("Building NCSD Header...")));
            Rom.Card2 = Card2;
            Rom.header = new NCSD.Header { Signature = new byte[0x100], Magic = 0x4453434E };
            ulong Length = 0x80 * 0x100000; // 128 MB
            while (Length <= Content.header.Size * MEDIA_UNIT_SIZE + 0x400000) //Extra 4 MB for potential save data
            {
                Length *= 2;
            }
            Rom.header.MediaSize = (uint)(Length / MEDIA_UNIT_SIZE);
            Rom.header.TitleId = Content.exheader.TitleID;
            Rom.header.OffsetSizeTable = new NCSD.NCCH_Meta[8];
            ulong OSOfs = 0x4000;
            for (int i = 0; i < Rom.header.OffsetSizeTable.Length; i++)
            {
                NCSD.NCCH_Meta ncchm = new NCSD.NCCH_Meta();
                if (i < Rom.NCCH_Array.Count)
                {
                    ncchm.Offset = (uint)(OSOfs / MEDIA_UNIT_SIZE);
                    ncchm.Size = Rom.NCCH_Array[i].header.Size;
                }
                else
                {
                    ncchm.Offset = 0;
                    ncchm.Size = 0;
                }
                Rom.header.OffsetSizeTable[i] = ncchm;
                OSOfs += ncchm.Size * MEDIA_UNIT_SIZE;
            }
            Rom.header.flags = new byte[0x8];
            Rom.header.flags[0] = 0; // 0-255 seconds of waiting for save writing.
            Rom.header.flags[3] = (byte)(Rom.Card2 ? 2 : 1); // Media Card Device: 1 = NOR Flash, 2 = None, 3 = BT
            Rom.header.flags[4] = 1; // Media Platform Index: 1 = CTR
            Rom.header.flags[5] = (byte)(Rom.Card2 ? 2 : 1); // Media Type Index: 0 = Inner Device, 1 = Card1, 2 = Card2, 3 = Extended Device
            Rom.header.flags[6] = 0; // Media Unit Size. Same as NCCH.
            Rom.header.flags[7] = 0; // Old Media Card Device.
            Rom.header.NCCHIdTable = new ulong[8];
            for (int i = 0; i < Rom.NCCH_Array.Count; i++)
            {
                Rom.header.NCCHIdTable[i] = Rom.NCCH_Array[i].header.TitleId;
            }
            Rom.cardinfoheader = new NCSD.CardInfoHeader
            {
                WritableAddress = (uint)(Rom.GetWritableAddress()),
                CardInfoBitmask = 0,
                CIN = new NCSD.CardInfoHeader.CardInfoNotes
                {
                    Reserved0 = new byte[0xF8],
                    MediaSizeUsed = OSOfs,
                    Reserved1 = 0,
                    Unknown = 0,
                    Reserved2 = new byte[0xC],
                    CVerTitleId = 0,
                    CVerTitleVersion = 0,
                    Reserved3 = new byte[0xCD6]
                },
                NCCH0TitleId = Rom.NCCH_Array[0].header.TitleId,
                Reserved0 = 0,
                InitialData = new byte[0x30]
            };
            RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider();
            byte[] randbuffer = new byte[0x2C];
            rng.GetBytes(randbuffer);
            Array.Copy(randbuffer, Rom.cardinfoheader.InitialData, randbuffer.Length);
            Rom.cardinfoheader.Reserved1 = new byte[0xC0];
            Rom.cardinfoheader.NCCH0Header = new byte[0x100];
            Array.Copy(Rom.NCCH_Array[0].header.Data, 0x100, Rom.cardinfoheader.NCCH0Header, 0, 0x100);

            Rom.BuildHeader();

            //NCSD is Initialized
            //Let's write this shit.
            WriteNCSD();
        }

        private void WriteNCSD()
        {
            using (FileStream OutFileStream = new FileStream(SAVE_PATH, FileMode.Create))
            {
                TB_Progress.Invoke((Action)(() => UpdateTB_Progress("Writing NCSD Header...")));
                OutFileStream.Write(Rom.Data, 0, Rom.Data.Length);
                TB_Progress.Invoke((Action)(() => UpdateTB_Progress("Writing NCCH...")));
                OutFileStream.Write(Rom.NCCH_Array[0].header.Data, 0, Rom.NCCH_Array[0].header.Data.Length); //Write NCCH header
                //AES time.
                byte[] key = new byte[0x10]; //Fixed-Crypto key is all zero.
                for (int i = 0; i < 3; i++)
                {
                    AesCtr aesctr = new AesCtr(key, Rom.NCCH_Array[0].header.ProgramId, ((ulong)(i + 1)) << 56); //CTR is ProgramID, section id<<88
                    switch (i)
                    {
                        case 0: //Exheader + AccessDesc
                            TB_Progress.Invoke((Action)(() => UpdateTB_Progress("Writing Exheader...")));
                            byte[] inEncExheader = new byte[Rom.NCCH_Array[0].exheader.Data.Length + Rom.NCCH_Array[0].exheader.AccessDescriptor.Length];
                            byte[] outEncExheader = new byte[Rom.NCCH_Array[0].exheader.Data.Length + Rom.NCCH_Array[0].exheader.AccessDescriptor.Length];
                            Array.Copy(Rom.NCCH_Array[0].exheader.Data, inEncExheader, Rom.NCCH_Array[0].exheader.Data.Length);
                            Array.Copy(Rom.NCCH_Array[0].exheader.AccessDescriptor, 0, inEncExheader, Rom.NCCH_Array[0].exheader.Data.Length, Rom.NCCH_Array[0].exheader.AccessDescriptor.Length);
                            aesctr.TransformBlock(inEncExheader, 0, inEncExheader.Length, outEncExheader, 0);
                            OutFileStream.Write(outEncExheader, 0, outEncExheader.Length); // Write Exheader
                            break;
                        case 1: //Exefs
                            TB_Progress.Invoke((Action)(() => UpdateTB_Progress("Writing Exefs...")));
                            OutFileStream.Seek(0x4000 + Rom.NCCH_Array[0].header.ExefsOffset * MEDIA_UNIT_SIZE, SeekOrigin.Begin);
                            byte[] OutExefs = new byte[Rom.NCCH_Array[0].exefs.Data.Length];
                            aesctr.TransformBlock(Rom.NCCH_Array[0].exefs.Data, 0, Rom.NCCH_Array[0].exefs.Data.Length, OutExefs, 0);
                            OutFileStream.Write(OutExefs, 0, OutExefs.Length);
                            break;
                        case 2: //Romfs
                            TB_Progress.Invoke((Action)(() => UpdateTB_Progress("Writing Romfs...")));
                            OutFileStream.Seek(0x4000 + Rom.NCCH_Array[0].header.RomfsOffset * MEDIA_UNIT_SIZE, SeekOrigin.Begin);
                            using (FileStream InFileStream = new FileStream(Rom.NCCH_Array[0].romfs.FileName, FileMode.Open, FileAccess.Read))
                            {
                                uint BUFFER_SIZE = 0;
                                ulong RomfsLen = Rom.NCCH_Array[0].header.RomfsSize * MEDIA_UNIT_SIZE;
                                PB_Show.Invoke((Action)(() =>
                                {
                                    PB_Show.Minimum = 0;
                                    PB_Show.Maximum = (int)(RomfsLen / 0x400000);
                                    PB_Show.Value = 0;
                                    PB_Show.Step = 1;
                                }));
                                for (ulong j = 0; j < (RomfsLen); j += BUFFER_SIZE)
                                {
                                    BUFFER_SIZE = (RomfsLen - j) > 0x400000 ? 0x400000 : (uint)(RomfsLen - j);
                                    byte[] buf = new byte[BUFFER_SIZE];
                                    byte[] outbuf = new byte[BUFFER_SIZE];
                                    InFileStream.Read(buf, 0, (int)BUFFER_SIZE);
                                    aesctr.TransformBlock(buf, 0, (int)BUFFER_SIZE, outbuf, 0);
                                    OutFileStream.Write(outbuf, 0, (int)BUFFER_SIZE);
                                    PB_Show.Invoke((Action)(() => PB_Show.PerformStep()));
                                }
                            }
                            break;
                    }
                }
                TB_Progress.Invoke((Action)(() => UpdateTB_Progress("Writing Logo...")));
                OutFileStream.Seek(0x4000 + Rom.NCCH_Array[0].header.LogoOffset * MEDIA_UNIT_SIZE, SeekOrigin.Begin);
                OutFileStream.Write(Rom.NCCH_Array[0].logo, 0, Rom.NCCH_Array[0].logo.Length);
                if (Rom.NCCH_Array[0].plainregion.Length > 0)
                {
                    TB_Progress.Invoke((Action)(() => UpdateTB_Progress("Writing Plain Region...")));
                    OutFileStream.Seek(0x4000 + Rom.NCCH_Array[0].header.PlainRegionOffset * MEDIA_UNIT_SIZE, SeekOrigin.Begin);
                    OutFileStream.Write(Rom.NCCH_Array[0].plainregion, 0, Rom.NCCH_Array[0].plainregion.Length);
                }

                //NCSD Padding
                OutFileStream.Seek(Rom.header.OffsetSizeTable[Rom.NCCH_Array.Count - 1].Offset * MEDIA_UNIT_SIZE + Rom.header.OffsetSizeTable[Rom.NCCH_Array.Count - 1].Size * MEDIA_UNIT_SIZE, SeekOrigin.Begin);
                ulong TotalLen = Rom.header.MediaSize * MEDIA_UNIT_SIZE;
                byte[] Buffer = Enumerable.Repeat((byte)0xFF, 0x400000).ToArray();
                TB_Progress.Invoke((Action)(() => UpdateTB_Progress("Writing NCSD Padding...")));
                while ((ulong)OutFileStream.Position < TotalLen)
                {
                    int BUFFER_LEN = ((TotalLen - (ulong)OutFileStream.Position) < 0x400000) ? (int)(TotalLen - (ulong)OutFileStream.Position) : 0x400000;
                    OutFileStream.Write(Buffer, 0, BUFFER_LEN);
                }
            }

            //Delete Temporary Romfs File If Necessary
            if (Content.romfs.isTempFile)
            {
                File.Delete(Content.romfs.FileName);
            }

            Invoke((Action)(() => B_Exefs.Enabled = B_Romfs.Enabled = B_Exheader.Enabled = B_SavePath.Enabled = CHK_Card2.Enabled = TB_Serial.Enabled = B_Go.Enabled = CB_Logo.Enabled = true));
            Invoke((Action)(() => Alert("Done!")));
        }

        private void BuildNCCH()
        {
            SHA256Managed sha = new SHA256Managed();
            Content = new NCCH();
            TB_Progress.Invoke((Action)(() => UpdateTB_Progress("Adding Exheader...")));
            Content.exheader = new Exheader(EXHEADER_PATH);
            Content.plainregion = new byte[0]; //No plain region by default.
            if (Content.exheader.isPokemon())
            {
                TB_Progress.Invoke((Action)(() => UpdateTB_Progress("Detected Pokemon Game. Adding Plain Region...")));
                if (Content.exheader.isXY())
                {
                    Content.plainregion = (byte[])Resources.ResourceManager.GetObject("XY");
                }
                else if (Content.exheader.isORAS())
                {
                    Content.plainregion = (byte[])Resources.ResourceManager.GetObject("ORAS");
                }
            }
            bool exefile = false;
            bool romfile = false;
            if (CHK_PrebuiltRomfs.InvokeRequired)
                CHK_PrebuiltRomfs.Invoke(new Action(() => romfile = CHK_PrebuiltRomfs.Checked));
            else
                romfile = CHK_PrebuiltRomfs.Checked;
            if (CHK_PrebuiltExefs.InvokeRequired)
                CHK_PrebuiltExefs.Invoke(new Action(() => exefile = CHK_PrebuiltExefs.Checked));
            else
                exefile = CHK_PrebuiltExefs.Checked;
            if (exefile)
            {
                TB_Progress.Invoke((Action)(() => UpdateTB_Progress("Using Pre-Built ExeFS...")));
                Content.exefs = new ExeFS(EXEFS_PATH, exefile);
            }
            else
            {
                TB_Progress.Invoke((Action)(() => UpdateTB_Progress("Building ExeFS...")));
                Content.exefs = new ExeFS(EXEFS_PATH, exefile);
            }
            if (romfile)
            {
                TB_Progress.Invoke((Action)(() => UpdateTB_Progress("Using Pre-Built RomFS...")));
                Content.romfs = new RomFS(ROMFS_PATH);
            }
            else
            {
                TB_Progress.Invoke((Action)(() => UpdateTB_Progress("Building RomFS...")));
                Content.romfs = new RomFS(ROMFS_PATH, PB_Show, TB_Progress);
            }
            TB_Progress.Invoke((Action)(() => UpdateTB_Progress("Adding Logo...")));
            Content.logo = (byte[])Resources.ResourceManager.GetObject(LOGO_NAME);
            TB_Progress.Invoke((Action)(() => UpdateTB_Progress("Building NCCH Header...")));
            ulong Len = 0x200; //NCCH Signature + NCCH Header
            Content.header = new NCCH.Header { Signature = new byte[0x100], Magic = 0x4843434E };
            Content.header.TitleId = Content.header.ProgramId = Content.exheader.TitleID;
            Content.header.MakerCode = 0x3130; //01
            Content.header.FormatVersion = 0x2; //Default
            Content.header.LogoHash = sha.ComputeHash(Content.logo);
            Content.header.ProductCode = Encoding.ASCII.GetBytes(TB_Serial.Text);
            Array.Resize(ref Content.header.ProductCode, 0x10);
            Content.header.ExheaderHash = Content.exheader.GetSuperBlockHash();
            Content.header.ExheaderSize = (uint)(Content.exheader.Data.Length);
            Len += Content.header.ExheaderSize + (uint)Content.exheader.AccessDescriptor.Length;
            Content.header.Flags = new byte[0x8];
            //FLAGS
            Content.header.Flags[3] = 0; // Crypto: 0 = <7.x, 1=7.x;
            Content.header.Flags[4] = 1; // Content Platform: 1 = CTR;
            Content.header.Flags[5] = 0x3; // Content Type Bitflags: 1=Data, 2=Executable, 4=SysUpdate, 8=Manual, 0x10=Trial;
            Content.header.Flags[6] = 0; // MEDIA_UNIT_SIZE = 0x200*Math.Pow(2, Content.header.Flags[6]);
            Content.header.Flags[7] = 1; // FixedCrypto = 1, NoMountRomfs = 2; NoCrypto=4;
            Content.header.LogoOffset = (uint)(Len / MEDIA_UNIT_SIZE);
            Content.header.LogoSize = (uint)(Content.logo.Length / MEDIA_UNIT_SIZE);
            Len += (uint)Content.logo.Length;
            Content.header.PlainRegionOffset = (uint)((Content.plainregion.Length > 0) ? Len / MEDIA_UNIT_SIZE : 0);
            Content.header.PlainRegionSize = (uint)Content.plainregion.Length / MEDIA_UNIT_SIZE;
            Len += (uint)Content.plainregion.Length;
            Content.header.ExefsOffset = (uint)(Len / MEDIA_UNIT_SIZE);
            Content.header.ExefsSize = (uint)(Content.exefs.Data.Length / MEDIA_UNIT_SIZE);
            Content.header.ExefsSuperBlockSize = 0x200 / MEDIA_UNIT_SIZE; //Static 0x200 for exefs superblock
            Len += (uint)Content.exefs.Data.Length;
            Len = (uint)Align(Len, 0x1000); //Romfs Start is aligned to 0x1000
            Content.header.RomfsOffset = (uint)(Len / MEDIA_UNIT_SIZE);
            Content.header.RomfsSize = (uint)((new FileInfo(Content.romfs.FileName)).Length / MEDIA_UNIT_SIZE);
            Content.header.RomfsSuperBlockSize = Content.romfs.SuperBlockLen / MEDIA_UNIT_SIZE;
            Len += Content.header.RomfsSize * MEDIA_UNIT_SIZE;
            Content.header.ExefsHash = Content.exefs.GetSuperBlockHash();
            Content.header.RomfsHash = Content.romfs.GetSuperBlockHash();
            Content.header.Size = (uint)(Len / MEDIA_UNIT_SIZE);
            //Build the Header byte[].
            Content.header.BuildHeader();
        }

        public void UpdateTB_Progress(string text)
        {
            TB_Progress.Text += text + Environment.NewLine;
        }

        public static ulong Align(ulong input, ulong alignsize)
        {
            ulong output = input;
            if (output % alignsize != 0)
            {
                output += (alignsize - (output % alignsize));
            }
            return output;
        }

        private void CB_Logo_SelectedIndexChanged(object sender, EventArgs e)
        {
            LOGO_NAME = CB_Logo.Text;
        }

        private void CHK_Card2_CheckedChanged(object sender, EventArgs e)
        {
            Card2 = CHK_Card2.Checked;
            if (Card2 == false)
            {
                MessageBox.Show("Note: NOR Flash (Card1) is not recommended for maximum compatibility.");
            }
            Validate_Go();
        }

        private void TB_Serial_TextChanged(object sender, EventArgs e)
        {
            TB_Serial.Text = TB_Serial.Text.ToUpper();
            Validate_Go();
        }

        private void CHK_PrebuiltRomfs_CheckedChanged(object sender, EventArgs e)
        {
            TB_Romfs.Text = string.Empty;
            Validate_Go();
        }

        private void CHK_PrebuiltExefs_CheckedChanged(object sender, EventArgs e)
        {
            TB_Exefs.Text = string.Empty;
            Validate_Go();
        }

        private void SetPrebuiltBoxes(bool en)
        {
            foreach (CheckBox c in new[] { CHK_PrebuiltExefs, CHK_PrebuiltRomfs })
            {
                if (c.InvokeRequired)
                    c.Invoke(new Action(() => c.Enabled = en));
                else
                    c.Enabled = en;
            }
        }


    }

    public class NCSD
    {
        public Header header;
        public CardInfoHeader cardinfoheader;
        public List<NCCH> NCCH_Array;

        public bool Card2;

        public byte[] Data;

        public class Header
        {
            public byte[] Signature; //Size 0x100;
            public uint Magic;
            public uint MediaSize;
            public ulong TitleId;
            //public byte[] padding; //Size: 0x10
            public NCCH_Meta[] OffsetSizeTable; //Size: 8
            //public byte[] padding; //Size: 0x28
            public byte[] flags; //Size: 0x8
            public ulong[] NCCHIdTable; //Size: 0x8;
            //public byte[] Padding2; //Size: 0x30;
        }

        public class CardInfoHeader
        {
            public uint WritableAddress;
            public uint CardInfoBitmask;
            public CardInfoNotes CIN;
            public ulong NCCH0TitleId;
            public ulong Reserved0;
            public byte[] InitialData; // Size: 0x30
            public byte[] Reserved1; // Size: 0xC0
            public byte[] NCCH0Header; // Size: 0x100

            public class CardInfoNotes
            {
                public byte[] Reserved0; // Size: 0xF8;
                public ulong MediaSizeUsed;
                public ulong Reserved1;
                public uint Unknown;
                public byte[] Reserved2; //Size: 0xC;
                public ulong CVerTitleId;
                public ushort CVerTitleVersion;
                public byte[] Reserved3; //Size: 0xCD6;
            }
        }

        public class NCCH_Meta
        {
            public uint Offset;
            public uint Size;
        }

        public ulong GetWritableAddress()
        {
            const ulong MEDIA_UNIT_SIZE = 0x200;
            return Card2
                ? (Form1.Align(header.OffsetSizeTable[NCCH_Array.Count - 1].Offset * NCCH.MEDIA_UNIT_SIZE
                    + header.OffsetSizeTable[NCCH_Array.Count - 1].Size * NCCH.MEDIA_UNIT_SIZE + 0x1000, 0x10000) / MEDIA_UNIT_SIZE)
                : 0x00000000FFFFFFFF;
        }

        public void BuildHeader()
        {
            Data = new byte[0x4000];
            Array.Copy(header.Signature, Data, 0x100);
            Array.Copy(BitConverter.GetBytes(header.Magic), 0, Data, 0x100, 4);
            Array.Copy(BitConverter.GetBytes(header.MediaSize), 0, Data, 0x104, 4);
            Array.Copy(BitConverter.GetBytes(header.TitleId), 0, Data, 0x108, 8);
            for (int i = 0; i < header.OffsetSizeTable.Length; i++)
            {
                Array.Copy(BitConverter.GetBytes(header.OffsetSizeTable[i].Offset), 0, Data, 0x120 + 8 * i, 4);
                Array.Copy(BitConverter.GetBytes(header.OffsetSizeTable[i].Size), 0, Data, 0x124 + 8 * i, 4);
            }
            Array.Copy(header.flags, 0, Data, 0x188, header.flags.Length);
            for (int i = 0; i < header.NCCHIdTable.Length; i++)
            {
                Array.Copy(BitConverter.GetBytes(header.NCCHIdTable[i]), 0, Data, 0x190 + 8 * i, 8);
            }
            //CardInfoHeader
            Array.Copy(BitConverter.GetBytes(cardinfoheader.WritableAddress), 0, Data, 0x200, 4);
            Array.Copy(BitConverter.GetBytes(cardinfoheader.CardInfoBitmask), 0, Data, 0x204, 4);
            Array.Copy(cardinfoheader.CIN.Reserved0, 0, Data, 0x208, cardinfoheader.CIN.Reserved0.Length);
            Array.Copy(BitConverter.GetBytes(cardinfoheader.CIN.MediaSizeUsed), 0, Data, 0x300, 8);
            Array.Copy(BitConverter.GetBytes(cardinfoheader.CIN.Reserved1), 0, Data, 0x308, 8);
            Array.Copy(BitConverter.GetBytes(cardinfoheader.CIN.Unknown), 0, Data, 0x310, 4);
            Array.Copy(cardinfoheader.CIN.Reserved2, 0, Data, 0x314, cardinfoheader.CIN.Reserved2.Length);
            Array.Copy(BitConverter.GetBytes(cardinfoheader.CIN.CVerTitleId), 0, Data, 0x320, 8);
            Array.Copy(BitConverter.GetBytes(cardinfoheader.CIN.CVerTitleVersion), 0, Data, 0x328, 2);
            Array.Copy(cardinfoheader.CIN.Reserved3, 0, Data, 0x32A, cardinfoheader.CIN.Reserved3.Length);
            Array.Copy(BitConverter.GetBytes(cardinfoheader.NCCH0TitleId), 0, Data, 0x1000, 8);
            Array.Copy(BitConverter.GetBytes(cardinfoheader.Reserved0), 0, Data, 0x1008, 8);
            Array.Copy(cardinfoheader.InitialData, 0, Data, 0x1010, cardinfoheader.InitialData.Length);
            Array.Copy(cardinfoheader.Reserved1, 0, Data, 0x1040, cardinfoheader.Reserved1.Length);
            Array.Copy(cardinfoheader.NCCH0Header, 0, Data, 0x1100, cardinfoheader.NCCH0Header.Length);
            Array.Copy(Enumerable.Repeat((byte)0xFF, 0x2E00).ToArray(), 0, Data, 0x1200, 0x2E00);
        }
    }

    public class NCCH
    {
        public Header header;
        public ExeFS exefs;
        public RomFS romfs;
        public Exheader exheader;
        public byte[] logo;
        public byte[] plainregion;

        public const uint MEDIA_UNIT_SIZE = 0x200;

        public class Header
        {
            public byte[] Signature; // Size: 0x100
            public uint Magic;
            public uint Size;
            public ulong TitleId;
            public ushort MakerCode;
            public ushort FormatVersion;
            // public uint padding0;
            public ulong ProgramId;
            // public byte[0x10] padding1;
            public byte[] LogoHash; // Size: 0x20
            public byte[] ProductCode; // Size: 0x10
            public byte[] ExheaderHash; // Size: 0x20
            public uint ExheaderSize;
            // public uint padding2;
            public byte[] Flags; // Size: 8
            public uint PlainRegionOffset;
            public uint PlainRegionSize;
            public uint LogoOffset;
            public uint LogoSize;
            public uint ExefsOffset;
            public uint ExefsSize;
            public uint ExefsSuperBlockSize;
            // public uint padding4;
            public uint RomfsOffset;
            public uint RomfsSize;
            public uint RomfsSuperBlockSize;
            // public uint padding5;
            public byte[] ExefsHash; // Size: 0x20
            public byte[] RomfsHash; // Size: 0x20

            public byte[] Data;

            public void BuildHeader()
            {
                Data = new byte[0x200];
                Array.Copy(Signature, Data, 0x100);
                Array.Copy(BitConverter.GetBytes(Magic), 0, Data, 0x100, 4);
                Array.Copy(BitConverter.GetBytes(Size), 0, Data, 0x104, 4);
                Array.Copy(BitConverter.GetBytes(TitleId), 0, Data, 0x108, 8);
                Array.Copy(BitConverter.GetBytes(MakerCode), 0, Data, 0x110, 2);
                Array.Copy(BitConverter.GetBytes(FormatVersion), 0, Data, 0x112, 2);
                // 4 Byte Padding
                Array.Copy(BitConverter.GetBytes(ProgramId), 0, Data, 0x118, 8);
                // 0x10 Byte Padding
                Array.Copy(LogoHash, 0, Data, 0x130, 0x20);
                Array.Copy(ProductCode, 0, Data, 0x150, 0x10);
                Array.Copy(ExheaderHash, 0, Data, 0x160, 0x20);
                Array.Copy(BitConverter.GetBytes(ExheaderSize), 0, Data, 0x180, 4);
                // 4 Byte Padding
                Array.Copy(Flags, 0, Data, 0x188, 0x8);
                uint ofs = 0x190;
                foreach (uint val in new uint[] { PlainRegionOffset, PlainRegionSize, LogoOffset, LogoSize, ExefsOffset, ExefsSize, ExefsSuperBlockSize, 0, RomfsOffset, RomfsSize, RomfsSuperBlockSize, 0 })
                {
                    Array.Copy(BitConverter.GetBytes(val), 0, Data, ofs, 4);
                    ofs += 4;
                }
                Array.Copy(ExefsHash, 0, Data, 0x1C0, 0x20);
                Array.Copy(RomfsHash, 0, Data, 0x1E0, 0x20);
            }
        }
    }
    public class ExeFS // A lot of this is lifted directly from pk3DS.
    {
        public byte[] Data;
        public string[] files;

        public ExeFS(string EXEFS_PATH, bool file)
        {
            if (file)
            {
                string tmpdir = Path.GetDirectoryName(Application.ExecutablePath) + Path.DirectorySeparatorChar + "tmpexedir" + Path.DirectorySeparatorChar;
                if (Directory.Exists(tmpdir))
                    Directory.Delete(tmpdir, true);
                Directory.CreateDirectory(tmpdir);
                get(EXEFS_PATH, tmpdir);
                files = (new DirectoryInfo(tmpdir)).GetFiles().Select(f => f.FullName).ToArray();
                SetData(files);
                Directory.Delete(tmpdir, true);
            }
            else
            {
                files = (new DirectoryInfo(EXEFS_PATH)).GetFiles().Select(f => f.FullName).ToArray();
                SetData(files);
            }
        }

        public byte[] GetSuperBlockHash()
        {
            SHA256Managed sha = new SHA256Managed();
            return sha.ComputeHash(Data, 0, 0x200);
        }

        internal static bool get(string inFile, string outPath)
        {
            try
            {
                byte[] data = File.ReadAllBytes(inFile);
                if (!Directory.Exists(outPath)) Directory.CreateDirectory(outPath);
                for (int i = 0; i < 10; i++)
                {
                    // Get File Name String; if exists we have a file to extract.
                    string fileName = Encoding.ASCII.GetString(data.Skip(0x10 * i).Take(0x8).ToArray()).TrimEnd((char)0);
                    if (fileName.Length > 0)
                        File.WriteAllBytes(
                            // New File Path
                            outPath + Path.DirectorySeparatorChar + fileName + ".bin",
                            // Get New Data from Offset after 0x200 Header.
                            data.Skip(0x200 + BitConverter.ToInt32(data, 0x8 + 0x10 * i)).Take(BitConverter.ToInt32(data, 0xC + 0x10 * i)).ToArray()
                            );
                }
                return true;
            }
            catch { return false; }
        }
        internal static bool set(string[] files, string outFile)
        {
            if (files.Length > 10) { Console.WriteLine("Cannot package more than 10 files to exefs."); return false; }

            try
            {
                // Set up the Header
                byte[] headerData = new byte[0x200];
                uint offset = 0;
                SHA256 sha = SHA256.Create();

                // Get the Header
                for (int i = 0; i < files.Length; i++)
                {
                    // Do the Top (File Info)
                    string fileName = Path.GetFileNameWithoutExtension(files[i]);
                    byte[] nameData = Encoding.ASCII.GetBytes(fileName); Array.Resize(ref nameData, 0x8);
                    Array.Copy(nameData, 0, headerData, i * 0x10, 0x8);

                    FileInfo fi = new FileInfo(files[i]);
                    uint size = (uint)fi.Length;
                    Array.Copy(BitConverter.GetBytes(offset), 0, headerData, 0x8 + i * 0x10, 0x4);
                    Array.Copy(BitConverter.GetBytes(size), 0, headerData, 0xC + i * 0x10, 0x4);
                    offset += (0x200 - size % 0x200) + size;

                    // Do the Bottom (Hashes)
                    byte[] hash = sha.ComputeHash(File.ReadAllBytes(files[i]));
                    Array.Copy(hash, 0, headerData, 0x200 - 0x20 * (i + 1), 0x20);
                }

                // Set in the Data
                using (MemoryStream newFile = new MemoryStream())
                {
                    new MemoryStream(headerData).CopyTo(newFile);
                    foreach (string s in files)
                    {
                        using (MemoryStream loadFile = new MemoryStream(File.ReadAllBytes(s)))
                            loadFile.CopyTo(newFile);
                        new MemoryStream(new byte[0x200 - newFile.Length % 0x200]).CopyTo(newFile);
                    }

                    File.WriteAllBytes(outFile, newFile.ToArray());
                }
                return true;
            }
            catch { return false; }
        }

        internal void SetData(string[] files)
        {
            // Set up the Header
            byte[] headerData = new byte[0x200];
            uint offset = 0;
            SHA256 sha = SHA256.Create();

            // Get the Header
            for (int i = 0; i < files.Length; i++)
            {
                // Do the Top (File Info)
                string fileName = Path.GetFileNameWithoutExtension(files[i]);
                byte[] nameData = Encoding.ASCII.GetBytes(fileName); Array.Resize(ref nameData, 0x8);
                Array.Copy(nameData, 0, headerData, i * 0x10, 0x8);

                FileInfo fi = new FileInfo(files[i]);
                uint size = (uint)fi.Length;
                Array.Copy(BitConverter.GetBytes(offset), 0, headerData, 0x8 + i * 0x10, 0x4);
                Array.Copy(BitConverter.GetBytes(size), 0, headerData, 0xC + i * 0x10, 0x4);
                offset += (0x200 - size % 0x200) + size;

                // Do the Bottom (Hashes)
                byte[] hash = sha.ComputeHash(File.ReadAllBytes(files[i]));
                Array.Copy(hash, 0, headerData, 0x200 - 0x20 * (i + 1), 0x20);
            }

            // Set in the Data
            using (MemoryStream newFile = new MemoryStream())
            {
                new MemoryStream(headerData).CopyTo(newFile);
                foreach (string s in files)
                {
                    using (MemoryStream loadFile = new MemoryStream(File.ReadAllBytes(s)))
                        loadFile.CopyTo(newFile);
                    new MemoryStream(new byte[0x200 - newFile.Length % 0x200]).CopyTo(newFile);
                }

                Data = newFile.ToArray();
            }
        }
    }
    public class RomFS // Adapted from RomFS Builder
    {
        public string FileName;
        public bool isTempFile;
        private byte[] SuperBlockHash;
        public uint SuperBlockLen;

        public RomFS(string fn)
        {
            FileName = fn;
            isTempFile = false;
            using (var fs = File.OpenRead(fn))
            {
                fs.Seek(0x8, SeekOrigin.Begin);
                uint mhlen = (uint)(fs.ReadByte() | (fs.ReadByte() << 8) | (fs.ReadByte() << 16) | (fs.ReadByte() << 24));
                SuperBlockLen = mhlen + 0x50;
                if (SuperBlockLen % 0x200 != 0)
                    SuperBlockLen += (0x200 - (SuperBlockLen % 0x200));
                byte[] superblock = new byte[SuperBlockLen];
                fs.Seek(0, SeekOrigin.Begin);
                fs.Read(superblock, 0, superblock.Length);
                using (SHA256 sha = SHA256.Create())
                {
                    SuperBlockHash = sha.ComputeHash(superblock);
                }
            }
        }

        public RomFS(string Dir, ProgressBar PB, TextBox TB)
        {
            Romfs_Builder rfsb = new Romfs_Builder(Dir, PB, TB);
            FileName = rfsb.GetFileName();
            isTempFile = true;
            SuperBlockHash = rfsb.GetSuperBlockHash();
            SuperBlockLen = rfsb.SuperBlockLen;
        }

        public byte[] GetSuperBlockHash()
        {
            return SuperBlockHash;
        }
    }
    public class Exheader
    {
        public byte[] Data;
        public byte[] AccessDescriptor;
        public ulong TitleID;

        public Exheader(string EXHEADER_PATH)
        {
            Data = File.ReadAllBytes(EXHEADER_PATH);
            AccessDescriptor = Data.Skip(0x400).Take(0x400).ToArray();
            Data = Data.Take(0x400).ToArray();
            TitleID = BitConverter.ToUInt64(Data, 0x200);
        }

        public byte[] GetSuperBlockHash()
        {
            SHA256Managed sha = new SHA256Managed();
            return sha.ComputeHash(Data, 0, 0x400);
        }

        public string GetSerial()
        {
            const string output = "CTR-P-";
            return output + Form1.RecognizedGames[TitleID][0];
        }

        public bool isORAS()
        {
            return (((TitleID & 0xFFFFFFFF) >> 8) == 0x11C5) || (((TitleID & 0xFFFFFFFF) >> 8) == 0x11C4);
        }
        public bool isXY()
        {
            return (((TitleID & 0xFFFFFFFF) >> 8) == 0x55D) || (((TitleID & 0xFFFFFFFF) >> 8) == 0x55E);
        }
        public bool isPokemon()
        {
            return (isORAS() || isXY());
        }
        public string GetPokemonSerial()
        {
            if (!isPokemon())
                return "CTR-P-XXXX";
            string name = string.Empty;
            switch ((TitleID & 0xFFFFFFFF) >> 8)
            {
                case 0x11C5: //Alpha Sapphire
                    name = "ECLA";
                    break;
                case 0x11C4: //Omega Ruby
                    name = "ECRA";
                    break;
                case 0x55D: //X
                    name = "EKJA";
                    break;
                case 0x55E: //Y
                    name = "EK2A";
                    break;
                default:
                    name = "XXXX";
                    break;
            }
            return "CTR-P-" + name;
        }
    }
}