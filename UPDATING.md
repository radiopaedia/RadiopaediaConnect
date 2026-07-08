# "Updating to a New Version" — source for radiopaedia.org

This is **not** end-user documentation in its own right. It's the source content for
section 10 ("Updating to a New Version") of the installation guide published at
[radiopaedia.org/radiopaediaconnect](https://radiopaedia.org/radiopaediaconnect),
kept here under version control since the live page is the canonical doc and isn't
itself in git. When this changes, paste the HTML block below over the existing
`<h4 id="10-updating-to-a-new-version">` section on that page (it ends at the next
`<hr>`), and bump the "Updated &lt;date&gt; (vX.X)" line at the bottom of the page.

It intentionally matches the conventions already used on that page: the
`~/rc-data` data directory, the `rp-code-wrapper`/`rp-code-block` markup for
copyable code blocks, and `-p 80:5000 -p 104:104` as the example port mapping.
It does **not** mention `RCONNECT_SCP_PORT` — that's a dev-only env var for
overriding the DICOM listener's *internal* container port, irrelevant to the
public docs where admins just remap the *host* side of `-p`.

---

```html
<h4 id="10-updating-to-a-new-version">Updating to a New Version</h4>

<p>Every image published to Docker Hub is tagged with the date it was built and
the commit it was built from (e.g. <code>radiopaediaorg/radiopaedia-connect:2026.07.01-a1b2c3d</code>),
as well as the floating <code>latest</code> tag. We recommend pinning your
deployment to a dated tag rather than <code>latest</code>: it lets you choose
exactly when you move to a new version, and means a quick <code>docker inspect</code>
on your running container always tells you what you're actually on.</p>

<h5 id="updating-check-version">Step 1: Check your current and available versions</h5>

<p>See which image your running container is using:</p>

<div class="rp-code-wrapper">
<div class="rp-code-header">Code block<button class="rp-copy-btn">Copy</button>
</div>
<pre class="rp-code-block rp-code-block--literal" data-literal-html="">
docker inspect --format='{{.Config.Image}}' radiopaedia-connect
</pre>
</div>

<p>Browse available versions on Docker Hub: <a href="https://hub.docker.com/r/radiopaediaorg/radiopaedia-connect/tags">hub.docker.com/r/radiopaediaorg/radiopaedia-connect/tags</a>.</p>

<h5 id="updating-backup">Step 2: Back up your data</h5>

<p>Your database and configuration live in the <code>~/rc-data</code> directory on
the host, not inside the container, so removing the old container does not
touch them. As a precaution before updating, stop the container and copy the
directory:</p>

<div class="rp-code-wrapper">
<div class="rp-code-header">Code block<button class="rp-copy-btn">Copy</button>
</div>
<pre class="rp-code-block rp-code-block--literal" data-literal-html="">
docker stop radiopaedia-connect
cp -r ~/rc-data ~/rc-data.bak-$(date +%Y%m%d)
</pre>
</div>

<h5 id="updating-apply">Step 3: Pull and run the new image</h5>

<ol>
	<li>Remove the existing container (your data is untouched): <code>docker rm radiopaedia-connect</code></li>
	<li>Pull the version you chose in Step 1, e.g.: <code>docker pull radiopaediaorg/radiopaedia-connect:2026.07.01-a1b2c3d</code></li>
	<li>Start the container again using your original <code>docker run</code> command, substituting the new tag for <code>latest</code> (or for the previous dated tag).</li>
</ol>

<blockquote><em><span style="font-family:lora;">Still running against <code>latest</code> from before dated tags existed? This is a good time to switch — just use a dated tag in place of <code>latest</code> from now on, so future updates are deliberate rather than whatever happened to be newest the last time you pulled.</span></em></blockquote>

<h5 id="updating-rollback">Rolling back</h5>

<p>Schema changes are applied automatically and additively on startup, so rolling
back to a previous dated tag against the same <code>~/rc-data</code> volume is
normally safe if you hit a problem after updating:</p>

<div class="rp-code-wrapper">
<div class="rp-code-header">Code block<button class="rp-copy-btn">Copy</button>
</div>
<pre class="rp-code-block rp-code-block--literal" data-literal-html="">
docker stop radiopaedia-connect
docker rm radiopaedia-connect
docker run -d --name radiopaedia-connect -p 80:5000 -p 104:104 -v ~/rc-data:/data --restart unless-stopped radiopaediaorg/radiopaedia-connect:&lt;previous-tag&gt;
</pre>
</div>

<p>If a release's notes call out a breaking database change, restore the backup
from Step 2 instead of rolling back the image alone.</p>
```
