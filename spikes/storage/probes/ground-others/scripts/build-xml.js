// The program the ground report's agent ran as `node -e` to print the XML document kdbxweb
// builds for a binary save, taken from the session transcript. Its code follows unchanged.
// It is run as `node -e <this file's text> <kdbxweb module directory> <database>`, which is
// why it reads its arguments from `process.argv[1]` and `process.argv[2]`; `run.ps1` does that.
// No double quote may appear here: Windows PowerShell passes the text as one quoted argument.
const k=require(process.argv[1]); const fs=require('fs'); const d=fs.readFileSync(process.argv[2]);
(async()=>{const db=await k.Kdbx.load(d.buffer.slice(d.byteOffset,d.byteOffset+d.byteLength), new k.Credentials(k.ProtectedValue.fromString('test')));
const ctx=new k.KdbxContext({kdbx: db}); db.buildXml(ctx); const s=k.XmlUtils.serialize(db.xml); const i=s.indexOf('<CustomData>'); console.log(s.substr(i,200)); const j=s.indexOf('<Times>'); console.log(s.substr(j,110));})().catch(e=>console.error(e));
