<h1 align="center"> Content Aware Funny Face Meme Generator </h1>

<p align="center">
<a href="#"><img src="https://img.shields.io/badge/GPU-Required-red.svg" alt="GPU"></a>

</p>

## GPU-accelerated content-aware image resizing for creating distorted videos.

<p align="center"> 
  <img src="Media/output_video3.gif" alt="description" width="200"> 
  <img src="Media/nice.gif" alt="description" width="200"> 
  
</p>

---

**[Releases](https://github.com/Panicdisress/seam-carving-meme-generator/releases)** page.*

---

### ✨ Key Features

* **⚡ GPU-Accelerated** – 10-20x faster than *Photoshop script method*.
* **🎯 Content-Aware** – Intelligent seam carving algorithm that preserves important image details.
* **🫠Facial Recognition** - Targets face for funny look.
* **🌀 Customizable Chaos** – Adjust **Jitter** and **seed** for unique, glitchy effects.
* **🎬 Built-in Video Export** – Integrated FFmpeg support to convert frames to MP4 instantly.
* **🖥️ User-Friendly GUI** – Simple Tkinter-based interface; no coding required to run.

---

| 10% Intensity | 30% intensity |
| :---: | :---: |
| <img src="Media/Men_Cry-10.gif" width="300"> | <img src="Media/Men_Cry-30.gif" width="300"> |
| **Time taken** | `1.4 sec.` |

## Fully 📸functional UI with inbuild video conversion.⬇️
<p align="center">
  <img src="Media/Interface.jpg" width="500" alt="App Interface">
</p>

---


---

## 🎛️ Parameter Guide
* **Img2Vid**

| Parameter | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| **Frames** | Integer | `60` | Total number of frames to generate for the animation sequence. |
| **Intensity** | Percentge | `40%` | The total amount of image width/height to carve away (10% to 80%). Higher = intense squish. |
| **Jitter** | Percentage | `25%` | Injects random noise into the energy map to create a chaotic, shaking, or boiling effect. |
| **Framerate** | FPS | `12` | The playback speed of the exported MP4 video. |

* **vid2vid (Uniform)**
 
| Parameter | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| Seed | Integer | `42` | Injects random noise into the algorithm for a glitchy, unstable visual effect. |
| Face Bias | Integer | `-50` | Directs the algorithm using facial recognition. -ve value effect more on face. +ve value protect face. |

* **vid2vid (Progressive)**
  
| Parameter | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| Max intensity | Percentage | `40%` | Peak distortion that the video will reach on its final frame. |




---

### 💡 Pro-Tips for Best Results

* **For Smooth Warping:** Keep `Jitter` at 0.
* **Resolution Tip:** For the fastest performance, use source images with a width under **600px**.
* **Preview Buttons** before doing heavy lifting of full animation render you can just preview frame results in both vid2vid methods.

### 📏 Image Size:
* less than / equal to  600px: lightning fast⚡
* 600-1200px: fast generation ✓
* greater than 1200px: slow 🐌

---

### 🛠️ Libraires:

* **OpenCV**: Image manipulation and frame handling.

* **FFmpeg**: Backend for high-quality video encoding.

---


## 🤖 AI Development Note

*I am not a coder. I don’t claim to be one, nor do I have the professional skillset. I am simply an enthusiast. I use AI to achieve my goals. Because I have a foundation in basic coding from school and college, I understand the logic well enough to guide AI models and steer them to fulfill my tasks. This project is the result of that partnership.*

### AI Contributions:
* **Optimization:** Algorithm refactoring and CUDA memory management.
* **Debugging:** Error correction and edge-case handling.
* **Documentation:** Drafting structure and technical explanations.
* **Fine-Tuning:** Parameter range suggestions and performance balancing.

### Human Oversight:
* ✅ **Validation:** All code was reviewed, tested, and validated by human developers.
* ✅ **Design:** Core algorithm logic and creative direction remain human-led.
* ✅ **Testing:** Performance benchmarks and manual validation conducted on local hardware.

---

## 🔧 Troubleshooting

### ❌ Slow Performance
* **Resolution:** High-res images (2K,4K+) scale exponentially in processing time.
* **Crash:** Batch is an very sensitive slider. Start with default then crank looking at taskbar.

---
