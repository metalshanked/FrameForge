"""Verify activation across Unix sessions and recovery after a killed primary."""
import os,pathlib,queue,subprocess,sys,threading,uuid
root=pathlib.Path(__file__).resolve().parents[2]
out=root/"artifacts/instance-checks"
out.mkdir(parents=True,exist_ok=True)
env=dict(os.environ,FRAMEFORGE_DESKTOP_DATA=str(out/uuid.uuid4().hex))
command=sys.argv[1:]
server=None
def start():
    global server
    server=subprocess.Popen(command,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True,env=env,start_new_session=True)
    ready=queue.Queue()
    threading.Thread(target=lambda: ready.put(server.stdout.readline()),daemon=True).start()
    assert ready.get(timeout=30).strip()=="ready","Primary did not start"
def activate():
    client=subprocess.run(command+["--capture"],capture_output=True,text=True,env=env,start_new_session=True,timeout=15)
    assert client.returncode==0 and client.stdout.strip()=="secondary",client.stdout+client.stderr
    output,error=server.communicate(timeout=15)
    assert server.returncode==0 and "capture-received" in output,output+error
try:
    start()
    activate()
    start()
    server.kill()
    server.wait()
    start()
    activate()
    (out/"results.txt").write_text("3 single-instance checks passed: independent sessions reuse the primary, capture requests reach it, and an abandoned instance can restart.\n")
    print((out/"results.txt").read_text())
finally:
    if server is not None and server.poll() is None:
        server.kill()
        server.wait()
