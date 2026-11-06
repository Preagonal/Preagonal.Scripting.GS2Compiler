#pragma once

#ifndef BUFFER_H
#define BUFFER_H

#include <cstdint>
#include <cassert>
#include <cstring>
#include <cstddef>
#include <cstdlib>
#include <utility>

class Buffer
{
    public:
		Buffer();
		Buffer(size_t len);
        Buffer(Buffer&& o) noexcept;
        Buffer(const Buffer&) = delete;
        ~Buffer();

        Buffer& operator=(const Buffer&) = delete;
        Buffer& operator= (Buffer&& o) noexcept;

        const uint8_t * buffer() const {
            return buf;
        }

        size_t length() const {
            return writepos;
        }

        size_t size() const {
            return buflen;
        }

        void setWritePos(size_t pos) {
            writepos = pos;
        }

        void read(char *dst, size_t len, size_t pos = 0) const;
        void write(const char *src, size_t len);
        void write(char val);
        void write(const Buffer& o);

        template<typename T>
        typename T::Val_Type Read(size_t pos) {
            return T::Read(*this, pos);
        }

        template<typename T>
        void Write(typename T::Val_Type val) {
            T::Write(*this, val);
        }

        template<typename T>
        void Write(typename T::Val_Type val, size_t pos) {
            auto tmp = writepos;
            writepos = pos;
            T::Write(*this, val);
            writepos = tmp;
        }

    private:
        void resize(size_t len = 0);

        uint8_t *buf;
        size_t buflen;
        size_t readpos, writepos;
};

inline Buffer::Buffer()
    : buf(nullptr), buflen(0), readpos(0), writepos(0)
{

}

inline Buffer::Buffer(size_t len)
    : Buffer()
{
    resize(len);
}

inline Buffer::Buffer(Buffer&& o) noexcept
    : Buffer()
{
    *this = std::move(o);
}

inline Buffer::~Buffer()
{
    if (buf)
    {
        free(buf);
    }
}

inline Buffer& Buffer::operator=(Buffer&& o) noexcept
{
	if (this == &o) return *this;
	free(buf);
	buf = o.buf;
    buflen = o.buflen;
    readpos = o.readpos;
    writepos = o.writepos;

    o.buf = nullptr;
    o.buflen = 0;
    o.readpos = 0;
    o.writepos = 0;
    return *this;
}

inline void Buffer::write(const Buffer& o)
{
    write((char *)o.buf, o.length());
}

inline void Buffer::resize(size_t len)
{
	if (buf)
	{
		auto tmp = buf;
		buflen = (len > buflen ? len : buflen * 2);
		buf = (uint8_t *)realloc(buf, buflen);
		assert(buf);
		
		// silencing msvc warning C6308
		if (!buf)
			free(tmp);
	}
	else
	{
		if (len == 0)
			len = 128;
		
		buf = (uint8_t *)malloc(len);
		buflen = len;
	}
}

inline void Buffer::read(char *dst, size_t len, size_t pos) const
{
	if (buflen > pos + len) {
		memcpy(dst, buf + pos, len);
	}
}

inline void Buffer::write(const char *src, size_t len)
{
	if (buflen < writepos + len)
		resize(buflen + writepos + len);

    memcpy(buf + writepos, src, len);
    writepos += len;
}

inline void Buffer::write(char val)
{
    if (buflen < writepos + 1)
        resize();

    buf[writepos++] = val;
}

#endif
